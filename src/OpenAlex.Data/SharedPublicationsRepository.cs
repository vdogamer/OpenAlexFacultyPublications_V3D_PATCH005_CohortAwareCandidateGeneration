using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class SharedPublicationsRepository
{
    private const int MaxUserIds = 25;
    private readonly SqlConnectionFactory _connectionFactory;

    public SharedPublicationsRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SharedPublicationSearchResult> FindSharedPublicationsAsync(
        IEnumerable<string> rawUserIds,
        CancellationToken cancellationToken = default)
    {
        var userIds = rawUserIds
            .Select(x => x?.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (userIds.Count < 2)
        {
            throw new ArgumentException("Enter at least two distinct userids.", nameof(rawUserIds));
        }

        if (userIds.Count > MaxUserIds)
        {
            throw new ArgumentException($"A maximum of {MaxUserIds} userids can be searched at once.", nameof(rawUserIds));
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.Faculty", "U", cancellationToken))
        {
            throw new InvalidOperationException("openalex.Faculty does not exist.");
        }

        if (!await ObjectExistsAsync(connection, "openalex.vw_FacultyPublicationExport", "V", cancellationToken))
        {
            throw new InvalidOperationException("openalex.vw_FacultyPublicationExport does not exist.");
        }

        var resolved = await ResolveFacultyAsync(connection, userIds, cancellationToken);
        var resolvedIds = resolved.Select(x => x.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = userIds.Where(x => !resolvedIds.Contains(x)).ToList();

        if (missing.Count > 0)
        {
            return new SharedPublicationSearchResult
            {
                ResolvedFaculty = resolved,
                MissingUserIds = missing
            };
        }

        var publications = await FindIntersectionAsync(connection, userIds, cancellationToken);

        return new SharedPublicationSearchResult
        {
            ResolvedFaculty = resolved,
            MissingUserIds = Array.Empty<string>(),
            Publications = publications
        };
    }

    private static async Task<IReadOnlyList<ResolvedFacultyUserId>> ResolveFacultyAsync(
        SqlConnection connection,
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<ResolvedFacultyUserId>();
        await using var command = connection.CreateCommand();

        var valueRows = AddUserIdParameters(command, userIds);

        command.CommandText = $@"
WITH Requested(UserId) AS
(
    SELECT UserId
    FROM (VALUES {valueRows}) AS v(UserId)
)
SELECT
    r.UserId,
    f.FacultyId,
    f.DisplayName,
    f.PrimaryEmail
FROM Requested r
OUTER APPLY
(
    SELECT TOP (1)
        f0.FacultyId,
        f0.DisplayName,
        f0.PrimaryEmail
    FROM openalex.Faculty f0
    WHERE f0.IsActive = 1
      AND f0.PrimaryEmail IS NOT NULL
      AND LOWER(LEFT(f0.PrimaryEmail, CHARINDEX('@', f0.PrimaryEmail + '@') - 1)) = r.UserId
    ORDER BY f0.FacultyId
) f
WHERE f.FacultyId IS NOT NULL
ORDER BY r.UserId;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ResolvedFacultyUserId
            {
                UserId = reader.GetString(0),
                FacultyId = reader.GetInt64(1),
                DisplayName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                PrimaryEmail = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SharedPublicationRow>> FindIntersectionAsync(
        SqlConnection connection,
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<SharedPublicationRow>();
        await using var command = connection.CreateCommand();

        var inParameters = new List<string>();
        for (var i = 0; i < userIds.Count; i++)
        {
            var parameterName = "@SearchUserId" + i;
            inParameters.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, userIds[i]);
        }

        command.Parameters.AddWithValue("@RequestedCount", userIds.Count);

        command.CommandText = $@"
SELECT
    WorkTitle,
    PublicationYear,
    PublicationDate,
    WorkDoi,
    DoiUrl,
    WorkOpenAlexId,
    WorkType,
    MAX(CitedByCount) AS CitedByCount,
    COUNT(DISTINCT LOWER(LEFT(PrimaryEmail, CHARINDEX('@', PrimaryEmail + '@') - 1))) AS SharedFacultyCount
FROM openalex.vw_FacultyPublicationExport
WHERE PrimaryEmail IS NOT NULL
  AND WorkOpenAlexId IS NOT NULL
  AND LOWER(LEFT(PrimaryEmail, CHARINDEX('@', PrimaryEmail + '@') - 1)) IN ({string.Join(", ", inParameters)})
  AND (DecisionStatus IS NULL OR DecisionStatus = 'Accepted')
GROUP BY
    WorkTitle,
    PublicationYear,
    PublicationDate,
    WorkDoi,
    DoiUrl,
    WorkOpenAlexId,
    WorkType
HAVING COUNT(DISTINCT LOWER(LEFT(PrimaryEmail, CHARINDEX('@', PrimaryEmail + '@') - 1))) = @RequestedCount
ORDER BY PublicationYear DESC, PublicationDate DESC, WorkTitle;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SharedPublicationRow
            {
                WorkTitle = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                PublicationYear = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                PublicationDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                WorkDoi = reader.IsDBNull(3) ? null : reader.GetString(3),
                DoiUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                WorkOpenAlexId = reader.IsDBNull(5) ? null : reader.GetString(5),
                WorkType = reader.IsDBNull(6) ? null : reader.GetString(6),
                CitedByCount = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                SharedFacultyCount = reader.GetInt32(8)
            });
        }

        return rows;
    }

    private static string AddUserIdParameters(SqlCommand command, IReadOnlyList<string> userIds)
    {
        var rows = new List<string>();
        for (var i = 0; i < userIds.Count; i++)
        {
            var parameterName = "@UserId" + i;
            rows.Add("(" + parameterName + ")");
            command.Parameters.AddWithValue(parameterName, userIds[i]);
        }

        return string.Join(", ", rows);
    }

    private static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
