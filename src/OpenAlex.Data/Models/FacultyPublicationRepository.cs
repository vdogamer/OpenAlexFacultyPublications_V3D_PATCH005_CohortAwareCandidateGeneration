using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyPublicationRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyPublicationRepository(
        SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }


    public async Task<FacultyPublicationProfile?>
        GetFacultyAsync(
            string facultyKey,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(facultyKey))
        {
            return null;
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT
    FacultyKey,
    DepartmentId,
    UvaUserId,
    DisplayName,
    Email,
    DepartmentName,
    DivisionName,
    Position,
    PublicationIdentitySource,
    SourceAuthorId,
    DataRunId,
    DepartmentDataComplete
FROM academiconline.vw_FacultyPublicationSearch
WHERE FacultyKey = @FacultyKey;";

        command.Parameters.AddWithValue(
            "@FacultyKey",
            facultyKey.Trim());

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return new FacultyPublicationProfile
        {
            FacultyKey =
                reader.GetString(0),

            DepartmentId =
                reader.GetInt64(1),

            UvaUserId =
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2),

            DisplayName =
                reader.GetString(3),

            Email =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),

            DepartmentName =
                reader.GetString(5),

            DivisionName =
                reader.IsDBNull(6)
                    ? null
                    : reader.GetString(6),

            Position =
                reader.IsDBNull(7)
                    ? null
                    : reader.GetString(7),

            PublicationIdentitySource =
                reader.GetString(8),

            SourceAuthorId =
                reader.GetString(9),

            DataRunId =
                reader.IsDBNull(10)
                    ? null
                    : reader.GetInt64(10),

            DepartmentDataComplete =
                !reader.IsDBNull(11) &&
                reader.GetBoolean(11)
        };
    }


    public async Task<IReadOnlyList<FacultyPublicationListItem>>
        GetPublicationsAsync(
            string facultyKey,
            int top = 500,
            CancellationToken cancellationToken = default)
    {
        var faculty =
            await GetFacultyAsync(
                facultyKey,
                cancellationToken);

        if (faculty is null ||
            !faculty.DataRunId.HasValue)
        {
            return Array.Empty<FacultyPublicationListItem>();
        }

        if (string.Equals(
                faculty.PublicationIdentitySource,
                "AcademicOnline",
                StringComparison.OrdinalIgnoreCase))
        {
            return await GetAcademicOnlinePublicationsAsync(
                faculty,
                top,
                cancellationToken);
        }

        if (string.Equals(
                faculty.PublicationIdentitySource,
                "OpenAlex",
                StringComparison.OrdinalIgnoreCase))
        {
            return await GetRosterPublicationsAsync(
                faculty,
                top,
                cancellationToken);
        }

        return Array.Empty<FacultyPublicationListItem>();
    }


    private async Task<IReadOnlyList<FacultyPublicationListItem>>
        GetAcademicOnlinePublicationsAsync(
            FacultyPublicationProfile faculty,
            int top,
            CancellationToken cancellationToken)
    {
        var rows =
            new List<FacultyPublicationListItem>();

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 1000)})
    p.PublicationKey,
    p.Title,
    p.Venue,
    p.YearText,
    p.PublicationYear,
    p.Doi,
    p.CitationCount,
    p.Link
FROM academiconline.DepartmentScholarPublication dsp
INNER JOIN academiconline.Publication p
    ON p.PublicationKey = dsp.PublicationKey
WHERE dsp.DepartmentId = @DepartmentId
  AND dsp.AcademicOnlineAuthorId = @AuthorId
  AND
  (
      (
          @DepartmentDataComplete = 1
          AND dsp.IsCurrent = 1
          AND dsp.CurrentSnapshotRunId = @DataRunId
      )
      OR
      (
          @DepartmentDataComplete = 0
          AND dsp.LastSyncRunId = @DataRunId
      )
  )
ORDER BY
    p.PublicationYear DESC,
    p.Title;";

        command.Parameters.AddWithValue(
            "@DepartmentId",
            faculty.DepartmentId);

        command.Parameters.AddWithValue(
            "@AuthorId",
            faculty.SourceAuthorId);

        command.Parameters.AddWithValue(
            "@DataRunId",
            faculty.DataRunId!.Value);

        command.Parameters.AddWithValue(
            "@DepartmentDataComplete",
            faculty.DepartmentDataComplete);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
            cancellationToken))
        {
            rows.Add(
                ReadPublication(reader));
        }

        return rows;
    }


    private async Task<IReadOnlyList<FacultyPublicationListItem>>
        GetRosterPublicationsAsync(
            FacultyPublicationProfile faculty,
            int top,
            CancellationToken cancellationToken)
    {
        var rows =
            new List<FacultyPublicationListItem>();

        if (!faculty.FacultyKey.StartsWith(
                "ROSTER:",
                StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var idText =
            faculty.FacultyKey[
                "ROSTER:".Length..];

        if (!long.TryParse(
                idText,
                out var rosterMemberId))
        {
            return rows;
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 1000)})
    p.PublicationKey,
    p.Title,
    p.Venue,
    p.YearText,
    p.PublicationYear,
    p.Doi,
    p.CitationCount,
    p.Link
FROM academiconline.DepartmentRosterPublication drp
INNER JOIN academiconline.Publication p
    ON p.PublicationKey = drp.PublicationKey
WHERE drp.DepartmentId = @DepartmentId
  AND drp.DepartmentRosterMemberId = @RosterMemberId
  AND
  (
      (
          @DepartmentDataComplete = 1
          AND drp.IsCurrent = 1
          AND drp.CurrentSnapshotRunId = @DataRunId
      )
      OR
      (
          @DepartmentDataComplete = 0
          AND drp.LastSyncRunId = @DataRunId
      )
  )
ORDER BY
    p.PublicationYear DESC,
    p.Title;";

        command.Parameters.AddWithValue(
            "@DepartmentId",
            faculty.DepartmentId);

        command.Parameters.AddWithValue(
            "@RosterMemberId",
            rosterMemberId);

        command.Parameters.AddWithValue(
            "@DataRunId",
            faculty.DataRunId!.Value);

        command.Parameters.AddWithValue(
            "@DepartmentDataComplete",
            faculty.DepartmentDataComplete);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                cancellationToken))
        {
            rows.Add(
                ReadPublication(reader));
        }

        return rows;
    }


    private static FacultyPublicationListItem
        ReadPublication(
            SqlDataReader reader)
    {
        return new FacultyPublicationListItem
        {
            PublicationKey =
                reader.GetInt64(0),

            Title =
                reader.IsDBNull(1)
                    ? "(Untitled publication)"
                    : reader.GetString(1),

            Venue =
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2),

            YearText =
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3),

            PublicationYear =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetInt32(4),

            Doi =
                reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5),

            CitationCount =
                reader.IsDBNull(6)
                    ? null
                    : reader.GetInt32(6),

            Link =
                reader.IsDBNull(7)
                    ? null
                    : reader.GetString(7)
        };
    }
}