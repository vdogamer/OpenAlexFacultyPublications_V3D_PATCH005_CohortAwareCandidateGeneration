using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public interface IFacultyDirectoryRepository
{
    Task<FacultyDirectoryPage> GetPageAsync(string? search = null, int pageNumber = 1,
        int pageSize = 100, CancellationToken cancellationToken = default);
}

/// <summary>A read-only directory; roster membership does not enroll anyone in matching jobs.</summary>
public sealed class FacultyDirectoryRepository : IFacultyDirectoryRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyDirectoryRepository(SqlConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<FacultyDirectoryPage> GetPageAsync(string? search = null, int pageNumber = 1,
        int pageSize = 100, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        pageNumber = Math.Max(1, pageNumber);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 200) search = search[..200];

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var hasRoster = await ExistsAsync(connection, "openalex.FacultyRoster", cancellationToken);
        var hasNative = await ExistsAsync(connection, "openalex.Faculty", cancellationToken);
        var hasRebuildImport = await ExistsAsync(connection, "academiconline.RebuildImportedFaculty", cancellationToken);
        if (!hasRoster && !hasNative && !hasRebuildImport)
            return new FacultyDirectoryPage(Array.Empty<FacultyListItem>(), 0, 0, 0, 1, pageSize);

        // Build only queries for existing tables. SQL Server binds absent objects even in IF branches.
        var native = hasNative
            ? BuildNativeSelect(await ColumnsAsync(connection, cancellationToken))
            : @"SELECT CAST(NULL AS bigint) FacultyId, CAST(NULL AS nvarchar(512)) ComputingId,
                CAST(NULL AS nvarchar(512)) DisplayName, CAST(NULL AS nvarchar(512)) PrimaryEmail,
                CAST(NULL AS nvarchar(512)) DepartmentName, CAST(NULL AS nvarchar(100)) DepartmentCode,
                CAST(NULL AS nvarchar(512)) DivisionName, CAST(NULL AS nvarchar(512)) AppointmentTitle,
                CAST(NULL AS nvarchar(256)) Orcid, CAST(NULL AS nvarchar(64)) ClaimedOpenAlexAuthorId,
                CAST(NULL AS bit) IsActive, CAST(0 AS bit) IsRosterMember,
                CAST(NULL AS nvarchar(40)) BaselineIdentityStatus,
                CAST(NULL AS nvarchar(512)) NativeSourceKeyFallback WHERE 1=0";
        var directory = hasRoster ? @"
SELECT r.NativeFacultyId FacultyId, r.ComputingId, r.DisplayName, r.DirectoryEmail PrimaryEmail,
       COALESCE(r.OrganizationName,r.DepartmentCode) DepartmentName, r.DepartmentCode,
       n.DivisionName, COALESCE(r.AppointmentTitle,r.AcademicRank) AppointmentTitle,
       n.Orcid, n.ClaimedOpenAlexAuthorId, n.IsActive,
       CAST(1 AS bit) IsRosterMember, r.BaselineIdentityStatus,
       CAST(NULL AS nvarchar(512)) NativeSourceKeyFallback
FROM openalex.FacultyRoster r
LEFT JOIN NativeFaculty n ON n.FacultyId=r.NativeFacultyId
UNION ALL
SELECT n.* FROM NativeFaculty n
WHERE NOT EXISTS (SELECT 1 FROM openalex.FacultyRoster r WHERE r.NativeFacultyId=n.FacultyId)"
            : "SELECT * FROM NativeFaculty";
        // Import evidence shares a display row only through a roster ComputingID,
        // a native ComputingID, or an exact canonical email when the native ID
        // is absent. A name, directory-email alias or SourceFacultyKey is not a link.
        const string importMatch = @"
((b.IsRosterMember=1 AND LOWER(LTRIM(RTRIM(b.ComputingId))) COLLATE Latin1_General_100_CI_AS=i.UserId)
 OR (b.IsRosterMember=0 AND
     (LOWER(LTRIM(RTRIM(b.ComputingId))) COLLATE Latin1_General_100_CI_AS=i.UserId
      OR (NULLIF(LTRIM(RTRIM(b.ComputingId)),N'') IS NULL
          AND LOWER(LTRIM(RTRIM(b.PrimaryEmail))) COLLATE Latin1_General_100_CI_AS=i.UserId+N'@virginia.edu'))))";
        var importedDirectory = hasRebuildImport ? $@"
SELECT b.*, CONVERT(bit,CASE WHEN i.UserId IS NULL THEN 0 ELSE 1 END) InRebuildImport,
       i.FacultyName ImportedDisplayName, i.PrimaryDepartment ImportedDepartmentName
FROM BaseDirectory b
LEFT JOIN academiconline.RebuildImportedFaculty i ON {importMatch}
UNION ALL
SELECT CAST(NULL AS bigint), i.UserId COLLATE DATABASE_DEFAULT, i.FacultyName,
       CAST(NULL AS nvarchar(512)), i.PrimaryDepartment, CAST(NULL AS nvarchar(100)),
       CAST(NULL AS nvarchar(512)), CAST(NULL AS nvarchar(512)),
       CAST(NULL AS nvarchar(256)), CAST(NULL AS nvarchar(64)),
       CAST(NULL AS bit), CAST(0 AS bit), CAST(NULL AS nvarchar(40)),
       CAST(NULL AS nvarchar(512)), CAST(1 AS bit), i.FacultyName, i.PrimaryDepartment
FROM academiconline.RebuildImportedFaculty i
WHERE NOT EXISTS (SELECT 1 FROM BaseDirectory b WHERE {importMatch})"
            : @"SELECT b.*, CAST(0 AS bit) InRebuildImport,
                CAST(NULL AS nvarchar(200)) ImportedDisplayName,
                CAST(NULL AS nvarchar(200)) ImportedDepartmentName FROM BaseDirectory b";
        var cte = $@"WITH NativeFaculty AS ({native}), BaseDirectory AS ({directory}),
Directory AS ({importedDirectory}),
Filtered AS (SELECT * FROM Directory WHERE @Search IS NULL
    OR DisplayName COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~'
    OR ComputingId COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~'
    OR PrimaryEmail COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~'
    OR DepartmentName COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~'
    OR DepartmentCode COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~'
    OR NativeSourceKeyFallback COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~'
    OR ImportedDisplayName COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~'
    OR ImportedDepartmentName COLLATE Latin1_General_100_CI_AS LIKE @Search ESCAPE N'~')";

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
DECLARE @Total int, @Roster int, @Native int, @Imported int;
{cte}
SELECT @Total=(SELECT COUNT(*) FROM Filtered),
       @Roster=(SELECT COUNT(*) FROM Directory WHERE IsRosterMember=1),
       @Native=(SELECT COUNT(*) FROM Directory WHERE IsRosterMember=0 AND FacultyId IS NOT NULL),
       @Imported=(SELECT COUNT(*) FROM Directory WHERE IsRosterMember=0 AND FacultyId IS NULL AND InRebuildImport=1);
DECLARE @LastPage int=CASE WHEN @Total=0 THEN 1 ELSE (@Total-1)/@PageSize+1 END;
DECLARE @CurrentPage int=CASE WHEN @PageNumber>@LastPage THEN @LastPage ELSE @PageNumber END;
SELECT @Total,@Roster,@Native,@CurrentPage,@Imported;
{cte}
SELECT * FROM Filtered
ORDER BY DisplayName,ComputingId,FacultyId
OFFSET ((@CurrentPage-1)*@PageSize) ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var escaped = search?.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 512).Value =
            escaped is null ? DBNull.Value : "%" + escaped + "%";
        command.Parameters.Add("@PageNumber", SqlDbType.Int).Value = pageNumber;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);
        var roster = reader.GetInt32(1);
        var additionalNative = reader.GetInt32(2);
        var currentPage = reader.GetInt32(3);
        var additionalImported = reader.GetInt32(4);
        await reader.NextResultAsync(cancellationToken);
        var rows = new List<FacultyListItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var isRosterMember = reader.GetBoolean(11);
            var email = Text(reader, 3);
            var computingId = Text(reader, 1);
            rows.Add(new FacultyListItem
            {
                FacultyId = reader.IsDBNull(0) ? null : reader.GetInt64(0),
                ComputingId = isRosterMember ? computingId : NormalizeNativeUserId(computingId)
                    ?? NormalizeNativeUserId(email),
                DisplayName = Text(reader, 2) ?? string.Empty,
                PrimaryEmail = email, DepartmentName = Text(reader, 4), DivisionName = Text(reader, 6),
                AppointmentTitle = Text(reader, 7), Orcid = Text(reader, 8),
                ClaimedOpenAlexAuthorId = Text(reader, 9),
                IsActive = reader.IsDBNull(10) ? null : reader.GetBoolean(10),
                IsRosterMember = isRosterMember, BaselineIdentityStatus = Text(reader, 12),
                InRebuildImport = reader.GetBoolean(14),
                ImportedDisplayName = Text(reader, 15), ImportedDepartmentName = Text(reader, 16)
            });
        }
        return new FacultyDirectoryPage(rows, total, roster, additionalNative, currentPage, pageSize)
            { AdditionalImportedCount = additionalImported };
    }

    private static string BuildNativeSelect(HashSet<string> columns)
    {
        string Field(string type, params string[] aliases)
        {
            var column = aliases.FirstOrDefault(columns.Contains);
            return column is null ? $"CAST(NULL AS {type})" : $"CAST([{column}] AS {type})";
        }
        var name = new[] { "DisplayName", "FacultyDisplayName", "FullName", "Name" }.FirstOrDefault(columns.Contains);
        if (name is null || !columns.Contains("FacultyId"))
            throw new InvalidOperationException("The native faculty schema is not supported.");
        return $@"SELECT CAST(FacultyId AS bigint) FacultyId,
            {Field("nvarchar(512)", "ComputingId")} ComputingId,
            {Field("nvarchar(512)", name)} DisplayName,
            {Field("nvarchar(512)", "PrimaryEmail", "Email")} PrimaryEmail,
            {Field("nvarchar(512)", "DepartmentName", "Department")} DepartmentName,
            CAST(NULL AS nvarchar(100)) DepartmentCode,
            {Field("nvarchar(512)", "DivisionName", "Division")} DivisionName,
            {Field("nvarchar(512)", "AppointmentTitle", "Title")} AppointmentTitle,
            {Field("nvarchar(256)", "Orcid", "ORCID")} Orcid,
            {Field("nvarchar(64)", "ClaimedOpenAlexAuthorId", "OpenAlexAuthorId", "AuthorOpenAlexId")} ClaimedOpenAlexAuthorId,
            {Field("bit", "IsActive", "Active")} IsActive, CAST(0 AS bit) IsRosterMember,
            CAST(NULL AS nvarchar(40)) BaselineIdentityStatus,
            {Field("nvarchar(512)", "SourceFacultyKey")} NativeSourceKeyFallback FROM openalex.Faculty";
    }

    private static string? NormalizeNativeUserId(string? value)
    {
        var id = value?.Trim().ToLowerInvariant();
        if (id?.EndsWith("@virginia.edu", StringComparison.Ordinal) == true) id = id[..^13];
        return id is not null && Regex.IsMatch(id, "^[a-z0-9]{2,32}$", RegexOptions.CultureInvariant) ? id : null;
    }

    private static string? Text(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    private static async Task<bool> ExistsAsync(SqlConnection connection, string name, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@Name,N'U') IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 256).Value = name;
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) == 1;
    }

    private static async Task<HashSet<string>> ColumnsAsync(SqlConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(N'openalex.Faculty')";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) columns.Add(reader.GetString(0));
        return columns;
    }
}
