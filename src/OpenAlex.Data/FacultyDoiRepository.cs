using System.Data;
using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public interface IFacultyDoiRepository
{
    Task<IReadOnlyList<FacultyDoiSourceRow>> GetByUserIdAsync(
        string userId, CancellationToken cancellationToken = default);
}

public sealed record FacultyDoiSourceRow(
    string FacultyName, string? Doi, string? Title, int? Year, string Source,
    bool IsRosterMember = false);

public sealed class FacultyDoiRepository : IFacultyDoiRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyDoiRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<FacultyDoiSourceRow>> GetByUserIdAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Schema checks run separately: SQL Server binds missing objects even in
        // unexecuted IF branches. Never execute a query for an absent source.
        var queries = new List<string>();
        var hasRoster = await HasObjectsAsync(connection, new[] { "openalex.FacultyRoster" }, cancellationToken);
        if (hasRoster)
        {
            queries.Add(@"
SELECT r.DisplayName, CAST(NULL AS nvarchar(512)) AS Doi,
       CAST(NULL AS nvarchar(max)) AS Title, CAST(NULL AS int) AS PublicationYear,
       N'Imported faculty roster' AS Source, CAST(1 AS bit) AS IsRosterMember
FROM openalex.FacultyRoster r
WHERE LOWER(LTRIM(RTRIM(r.ComputingId))) = @UserId");
        }
        if (await HasObjectsAsync(connection, new[]
            {
                "academiconline.Scholar", "academiconline.Department",
                "academiconline.DepartmentScholarPublication",
                "academiconline.vw_DepartmentNewestPublication"
            }, cancellationToken))
        {
            queries.Add(@"
SELECT s.DisplayName, p.Doi, p.Title, p.PublicationYear,
       N'Synchronized publications' AS Source, CAST(0 AS bit) AS IsRosterMember
FROM academiconline.Scholar s
LEFT JOIN
(
    SELECT DISTINCT sp.AcademicOnlineAuthorId, v.PublicationKey,
           v.Doi, v.Title, v.PublicationYear
    FROM academiconline.DepartmentScholarPublication sp
    JOIN academiconline.Department d ON d.DepartmentId = sp.DepartmentId
    JOIN academiconline.vw_DepartmentNewestPublication v
      ON v.DepartmentId = sp.DepartmentId AND v.PublicationKey = sp.PublicationKey
    WHERE sp.IsCurrent = 1
      AND sp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
      AND d.SourceMode <> N'FacultyRoster'
) p ON p.AcademicOnlineAuthorId = s.AcademicOnlineAuthorId
WHERE LOWER(LTRIM(RTRIM(s.UvaUserId))) = @UserId
   OR LOWER(LTRIM(RTRIM(s.Email))) = @Email");
        }

        if (await HasObjectsAsync(connection, new[]
            {
                "academiconline.DepartmentRosterMember", "academiconline.Department",
                "academiconline.DepartmentRosterPublication",
                "academiconline.vw_DepartmentNewestPublication"
            }, cancellationToken))
        {
            queries.Add(@"
SELECT rm.DisplayName, p.Doi, p.Title, p.PublicationYear,
       N'Synchronized publications' AS Source, CAST(0 AS bit) AS IsRosterMember
FROM academiconline.DepartmentRosterMember rm
LEFT JOIN
(
    SELECT DISTINCT rp.DepartmentRosterMemberId, v.PublicationKey,
           v.Doi, v.Title, v.PublicationYear
    FROM academiconline.DepartmentRosterPublication rp
    JOIN academiconline.Department d ON d.DepartmentId = rp.DepartmentId
    JOIN academiconline.vw_DepartmentNewestPublication v
      ON v.DepartmentId = rp.DepartmentId AND v.PublicationKey = rp.PublicationKey
    WHERE rp.IsCurrent = 1
      AND rp.CurrentSnapshotRunId = d.CurrentSnapshotRunId
      AND d.SourceMode = N'FacultyRoster'
) p ON p.DepartmentRosterMemberId = rm.DepartmentRosterMemberId
WHERE rm.IsActive = 1
  AND LOWER(LTRIM(RTRIM(rm.ExpectedEmail))) = @Email
");
        }

        if (await HasObjectsAsync(connection, new[]
            {
                "openalex.Faculty", "openalex.FacultyPublicationDecision",
                "openalex.FacultyPublicationCandidate"
            }, cancellationToken))
        {
            await using var columnCheck = connection.CreateCommand();
            columnCheck.CommandText =
                "SELECT CASE WHEN COL_LENGTH(N'openalex.Faculty', N'SourceFacultyKey') IS NULL THEN 0 ELSE 1 END;";
            var sourceKeyClause = Convert.ToInt32(
                await columnCheck.ExecuteScalarAsync(cancellationToken)) == 1
                ? " OR LOWER(LTRIM(RTRIM(f.SourceFacultyKey))) IN (@UserId,@Email)"
                : string.Empty;
            // Follow only the native link already confirmed by the roster import.
            // Do not infer a link from a name or a directory-email alias here.
            var rosterLinkClause = hasRoster ? @"
 OR EXISTS (SELECT 1 FROM openalex.FacultyRoster r
            WHERE LOWER(LTRIM(RTRIM(r.ComputingId)))=@UserId AND r.NativeFacultyId=f.FacultyId)" : string.Empty;

            queries.Add(@"
SELECT f.DisplayName, p.Doi, p.Title, p.PublicationYear,
       N'Accepted OpenAlex publications' AS Source, CAST(0 AS bit) AS IsRosterMember
FROM openalex.Faculty f
LEFT JOIN
(
    SELECT d.FacultyId, COALESCE(NULLIF(LTRIM(RTRIM(d.WorkDoi)), N''), pc.WorkDoi) AS Doi,
           COALESCE(d.WorkTitle, pc.WorkTitle) AS Title,
           COALESCE(d.PublicationYear, pc.PublicationYear) AS PublicationYear
    FROM openalex.FacultyPublicationDecision d
    LEFT JOIN openalex.FacultyPublicationCandidate pc
      ON pc.FacultyPublicationCandidateId = d.FacultyPublicationCandidateId
    WHERE d.IsCurrent = 1 AND d.DecisionStatus = N'Accepted'
) p ON p.FacultyId = f.FacultyId
WHERE LOWER(LTRIM(RTRIM(f.PrimaryEmail))) = @Email" + sourceKeyClause + rosterLinkClause);
        }

        if (queries.Count == 0)
        {
            throw new InvalidOperationException("No supported local faculty or publication source is configured.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = string.Join("\nUNION ALL\n", queries);
        command.Parameters.Add("@UserId", SqlDbType.NVarChar, 32).Value = userId;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 512).Value = userId + "@virginia.edu";

        var rows = new List<FacultyDoiSourceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FacultyDoiSourceRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetString(4), reader.GetBoolean(5)));
        }

        return rows;
    }

    private static async Task<bool> HasObjectsAsync(
        SqlConnection connection, IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN " + string.Join(" AND ",
            names.Select((_, index) => $"OBJECT_ID(@Object{index}) IS NOT NULL")) +
            " THEN 1 ELSE 0 END;";
        for (var index = 0; index < names.Count; index++)
        {
            command.Parameters.Add($"@Object{index}", SqlDbType.NVarChar, 256).Value = names[index];
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
