using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace AcademicOnline.CitationPilot;

internal static class DatabaseReader
{
    internal const string Sql = """
        SELECT CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerName,
            DB_NAME() AS DatabaseName, SYSUTCDATETIME() AS ReadUtc,
            (SELECT COUNT_BIG(*) FROM dbo.[User] WHERE UserId=@UserId) AS FacultyRows,
            (SELECT COUNT_BIG(*) FROM dbo.DOI WHERE UserId=@UserId) AS DoiRows;
        SELECT u.UserId, u.FacultyName, u.PrimaryDepartment, d.UserId AS DoiOwner, d.DOI
        FROM dbo.[User] AS u LEFT JOIN dbo.DOI AS d ON d.UserId=u.UserId
        WHERE u.UserId=@UserId ORDER BY d.DOI;
        SELECT N'CompleteOneFacultyRead' AS EndMarker;
        """;

    public static async Task<DatabaseReadResult> ReadAsync(string connectionString, string userId, CancellationToken ct)
    {
        userId = DoiRules.UserId(userId);
        var started = DateTimeOffset.UtcNow;
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        if (!connection.Database.Equals("AcademicOnline_Rebuild", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The citation pilot requires AcademicOnline_Rebuild. Check the existing FacultyDatabase setting.");
        // Hold the one-ID ranges only while reading counts and rows. Commit before any network lookup.
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        using var command = new SqlCommand(Sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("@UserId", SqlDbType.VarChar, 32).Value = userId;
        DatabaseReadResult result;
        await using (var reader = await command.ExecuteReaderAsync(ct))
            result = await ParseAsync(reader, userId, started, ct);
        await transaction.CommitAsync(ct);
        return result with { Snapshot = result.Snapshot with { ReadCompletedUtc = DateTimeOffset.UtcNow } };
    }

    internal static async Task<DatabaseReadResult> ParseAsync(DbDataReader reader, string userId,
        DateTimeOffset started, CancellationToken ct)
    {
        if (!await reader.ReadAsync(ct)) throw new FormatException("Missing database export header.");
        var server = reader.IsDBNull(0) ? "Not supplied" : reader.GetString(0);
        var database = reader.GetString(1);
        if (!database.Equals("AcademicOnline_Rebuild", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The database export header identifies a different database.");
        var facultyCount = reader.GetInt64(3);
        var doiCount = reader.GetInt64(4);
        if (facultyCount == 0) throw new FormatException("No faculty record matches this ComputingID. No empty publication list was inferred.");
        if (facultyCount != 1 || doiCount is < 0 or > 10000)
            throw new FormatException("Unexpected faculty/DOI count, or more than 10,000 associations for this pilot. No truncated export was produced.");
        if (await reader.ReadAsync(ct) || !await reader.NextResultAsync(ct)) throw new FormatException("Malformed database export header.");
        FacultyIdentity? faculty = null;
        var raw = new List<string?>();
        var selectedRows = 0;
        while (await reader.ReadAsync(ct))
        {
            selectedRows++;
            var identity = new FacultyIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            if (!identity.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase)
                || (faculty is not null && identity != faculty))
                throw new FormatException("The database rows disagree about the selected faculty identity.");
            faculty = identity;
            if (!reader.IsDBNull(3))
            {
                if (!reader.GetString(3).Equals(userId, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("A DOI association belongs to another ComputingID.");
                raw.Add(reader.IsDBNull(4) ? null : reader.GetString(4));
            }
        }
        if (faculty is null || raw.Count != doiCount || selectedRows != Math.Max(1L, doiCount))
            throw new FormatException("The database DOI row count is incomplete or inconsistent.");
        if (!await reader.NextResultAsync(ct) || !await reader.ReadAsync(ct)
            || reader.GetString(0) != "CompleteOneFacultyRead" || await reader.ReadAsync(ct)
            || await reader.NextResultAsync(ct))
            throw new FormatException("Missing or unexpected database export completion marker.");
        return new(new(Guid.NewGuid(), server, database, started, DateTimeOffset.UtcNow, 1, raw.Count,
            true, "CompleteOneFacultyRead"), faculty, raw.ToArray());
    }
}
