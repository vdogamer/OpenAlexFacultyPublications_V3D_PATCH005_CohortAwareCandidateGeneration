using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyPublicationFreshnessRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyPublicationFreshnessRepository(
        SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<FacultyPublicationFreshnessIdentity>>
        GetFacultyIdentitiesByUvaUserIdAsync(
            string uvaUserId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uvaUserId))
        {
            return Array.Empty<FacultyPublicationFreshnessIdentity>();
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.vw_FacultyPublicationSearch",
                "V",
                cancellationToken))
        {
            return Array.Empty<FacultyPublicationFreshnessIdentity>();
        }

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT
    f.FacultyKey,
    f.DepartmentId,
    f.UvaUserId,
    f.DisplayName,
    f.Email,
    f.DepartmentName,
    f.DivisionName,
    f.Position,
    f.PublicationIdentitySource,
    f.SourceAuthorId,

    AcademicOnlineAuthorId =
        COALESCE(
            NULLIF(LTRIM(RTRIM(roster.AcademicOnlineAuthorId)), N''),
            CASE
                WHEN f.PublicationIdentitySource = N'AcademicOnline'
                    THEN NULLIF(LTRIM(RTRIM(f.SourceAuthorId)), N'')
                ELSE NULL
            END
        ),

    OpenAlexAuthorId =
        COALESCE(
            NULLIF(LTRIM(RTRIM(roster.OpenAlexAuthorId)), N''),
            CASE
                WHEN f.PublicationIdentitySource = N'OpenAlex'
                    THEN NULLIF(LTRIM(RTRIM(f.SourceAuthorId)), N'')
                ELSE NULL
            END
        ),

    roster.OpenAlexResolutionStatus,
    f.DataRunId,
    f.DepartmentDataComplete

FROM academiconline.vw_FacultyPublicationSearch f

OUTER APPLY
(
    SELECT TOP (1)
        rm.AcademicOnlineAuthorId,
        rm.OpenAlexAuthorId,
        rm.OpenAlexResolutionStatus

    FROM academiconline.DepartmentRosterMember rm

    WHERE rm.DepartmentId = f.DepartmentId
      AND rm.IsActive = 1
      AND
      (
          (
              f.FacultyKey LIKE N'ROSTER:%'
              AND rm.DepartmentRosterMemberId =
                  TRY_CONVERT(
                      BIGINT,
                      SUBSTRING(
                          f.FacultyKey,
                          LEN(N'ROSTER:') + 1,
                          50))
          )

          OR

          (
              NULLIF(LTRIM(RTRIM(f.Email)), N'') IS NOT NULL
              AND LOWER(LTRIM(RTRIM(rm.ExpectedEmail))) =
                  LOWER(LTRIM(RTRIM(f.Email)))
          )

          OR

          (
              f.PublicationIdentitySource = N'AcademicOnline'
              AND NULLIF(LTRIM(RTRIM(rm.AcademicOnlineAuthorId)), N'') =
                  NULLIF(LTRIM(RTRIM(f.SourceAuthorId)), N'')
          )
      )

    ORDER BY
        CASE
            WHEN f.FacultyKey LIKE N'ROSTER:%'
             AND rm.DepartmentRosterMemberId =
                 TRY_CONVERT(
                     BIGINT,
                     SUBSTRING(
                         f.FacultyKey,
                         LEN(N'ROSTER:') + 1,
                         50))
                THEN 0

            WHEN f.PublicationIdentitySource = N'AcademicOnline'
             AND NULLIF(LTRIM(RTRIM(rm.AcademicOnlineAuthorId)), N'') =
                 NULLIF(LTRIM(RTRIM(f.SourceAuthorId)), N'')
                THEN 1

            ELSE 2
        END,
        rm.DepartmentRosterMemberId
) roster

WHERE LOWER(LTRIM(RTRIM(f.UvaUserId))) = @UvaUserId

ORDER BY
    f.DepartmentName,
    f.FacultyKey;";

        command.Parameters.AddWithValue(
            "@UvaUserId",
            uvaUserId.Trim().ToLowerInvariant());

        var rows =
            new List<FacultyPublicationFreshnessIdentity>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new FacultyPublicationFreshnessIdentity
                {
                    FacultyKey = reader.GetString(0),
                    DepartmentId = reader.GetInt64(1),
                    UvaUserId = GetNullableString(reader, 2),
                    DisplayName = reader.GetString(3),
                    Email = GetNullableString(reader, 4),
                    DepartmentName = reader.GetString(5),
                    DivisionName = GetNullableString(reader, 6),
                    Position = GetNullableString(reader, 7),
                    PublicationIdentitySource = reader.GetString(8),
                    SourceAuthorId = reader.GetString(9),
                    AcademicOnlineAuthorId = GetNullableString(reader, 10),
                    OpenAlexAuthorId = GetNullableString(reader, 11),
                    OpenAlexResolutionStatus = GetNullableString(reader, 12),
                    DataRunId = reader.IsDBNull(13)
                        ? null
                        : reader.GetInt64(13),
                    DepartmentDataComplete =
                        !reader.IsDBNull(14) &&
                        reader.GetBoolean(14)
                });
        }

        return rows;
    }

    public async Task<IReadOnlyList<FacultyPublicationFreshnessReviewState>>
        GetReviewStatesByUvaUserIdAsync(
            string uvaUserId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uvaUserId))
        {
            return Array.Empty<FacultyPublicationFreshnessReviewState>();
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.vw_FacultyPublicationReviewStatus",
                "V",
                cancellationToken))
        {
            return Array.Empty<FacultyPublicationFreshnessReviewState>();
        }

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT
    Doi =
        COALESCE(
            NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
            NULLIF(LTRIM(RTRIM(q.Doi)), N'')
        ),
    q.Title,
    q.PublicationYear,
    q.ReviewStatus,
    q.DecisionStatus,
    q.IsCurrentSource
FROM academiconline.vw_FacultyPublicationReviewStatus q
WHERE LOWER(LTRIM(RTRIM(q.UvaUserId))) = @UvaUserId
ORDER BY
    q.PublicationYear DESC,
    q.Title,
    q.FacultyPublicationReviewCandidateId DESC;";

        command.Parameters.AddWithValue(
            "@UvaUserId",
            uvaUserId.Trim().ToLowerInvariant());

        var rows =
            new List<FacultyPublicationFreshnessReviewState>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new FacultyPublicationFreshnessReviewState
                {
                    Doi = GetNullableString(reader, 0),
                    Title = reader.IsDBNull(1)
                        ? string.Empty
                        : reader.GetString(1),
                    PublicationYear = reader.IsDBNull(2)
                        ? null
                        : reader.GetInt32(2),
                    ReviewStatus = reader.IsDBNull(3)
                        ? string.Empty
                        : reader.GetString(3),
                    DecisionStatus = GetNullableString(reader, 4),
                    IsCurrentSource =
                        !reader.IsDBNull(5) &&
                        reader.GetBoolean(5)
                });
        }

        return rows;
    }

    public async Task<IReadOnlyList<FacultyPublicationDoiReviewAssociation>>
        GetReviewAssociationsByDoiAsync(
            string normalizedDoi,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedDoi))
        {
            return Array.Empty<FacultyPublicationDoiReviewAssociation>();
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.vw_FacultyPublicationReviewStatus",
                "V",
                cancellationToken))
        {
            return Array.Empty<FacultyPublicationDoiReviewAssociation>();
        }

        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT DISTINCT
    UvaUserId = LOWER(LTRIM(RTRIM(q.UvaUserId))),
    q.Title,
    q.PublicationYear,
    q.ReviewStatus,
    q.DecisionStatus,
    q.IsCurrentSource
FROM academiconline.vw_FacultyPublicationReviewStatus q
WHERE NULLIF(LTRIM(RTRIM(q.UvaUserId)), N'') IS NOT NULL
  AND LTRIM(RTRIM(
        REPLACE(
            REPLACE(
                REPLACE(
                    REPLACE(
                        LOWER(
                            COALESCE(
                                NULLIF(LTRIM(RTRIM(q.DoiNormalized)), N''),
                                NULLIF(LTRIM(RTRIM(q.Doi)), N'')
                            )
                        ),
                        N'https://dx.doi.org/', N''
                    ),
                    N'https://doi.org/', N''
                ),
                N'http://doi.org/', N''
            ),
            N'doi:', N''
        )
      )) = @Doi
ORDER BY
    q.PublicationYear DESC,
    q.Title;";

        command.Parameters.AddWithValue(
            "@Doi",
            normalizedDoi.Trim().ToLowerInvariant());

        var rows =
            new List<FacultyPublicationDoiReviewAssociation>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new FacultyPublicationDoiReviewAssociation
                {
                    UvaUserId = reader.GetString(0),
                    Title = reader.IsDBNull(1)
                        ? string.Empty
                        : reader.GetString(1),
                    PublicationYear = reader.IsDBNull(2)
                        ? null
                        : reader.GetInt32(2),
                    ReviewStatus = reader.IsDBNull(3)
                        ? string.Empty
                        : reader.GetString(3),
                    DecisionStatus = GetNullableString(reader, 4),
                    IsCurrentSource =
                        !reader.IsDBNull(5) &&
                        reader.GetBoolean(5)
                });
        }

        return rows;
    }

    public async Task<IReadOnlyList<string>>
        GetUvaUserIdsByTrustedOpenAlexAuthorIdsAsync(
            IReadOnlyCollection<string> openAlexAuthorIds,
            CancellationToken cancellationToken = default)
    {
        var authorIds = openAlexAuthorIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (authorIds.Length == 0)
        {
            return Array.Empty<string>();
        }

        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(
                connection,
                "academiconline.vw_FacultyPublicationSearch",
                "V",
                cancellationToken))
        {
            return Array.Empty<string>();
        }

        await using var command =
            connection.CreateCommand();

        var parameterNames = new List<string>();

        for (var index = 0; index < authorIds.Length; index++)
        {
            var parameterName = $"@OpenAlexAuthorId{index}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(
                parameterName,
                authorIds[index]);
        }

        var inClause =
            string.Join(", ", parameterNames);

        command.CommandText = $@"
SELECT DISTINCT
    UvaUserId = LOWER(LTRIM(RTRIM(f.UvaUserId)))
FROM academiconline.vw_FacultyPublicationSearch f
OUTER APPLY
(
    SELECT TOP (1)
        rm.OpenAlexAuthorId,
        rm.OpenAlexResolutionStatus
    FROM academiconline.DepartmentRosterMember rm
    WHERE rm.DepartmentId = f.DepartmentId
      AND rm.IsActive = 1
      AND
      (
          (
              f.FacultyKey LIKE N'ROSTER:%'
              AND rm.DepartmentRosterMemberId =
                  TRY_CONVERT(
                      BIGINT,
                      SUBSTRING(
                          f.FacultyKey,
                          LEN(N'ROSTER:') + 1,
                          50))
          )
          OR
          (
              NULLIF(LTRIM(RTRIM(f.Email)), N'') IS NOT NULL
              AND LOWER(LTRIM(RTRIM(rm.ExpectedEmail))) =
                  LOWER(LTRIM(RTRIM(f.Email)))
          )
          OR
          (
              f.PublicationIdentitySource = N'AcademicOnline'
              AND NULLIF(LTRIM(RTRIM(rm.AcademicOnlineAuthorId)), N'') =
                  NULLIF(LTRIM(RTRIM(f.SourceAuthorId)), N'')
          )
      )
    ORDER BY
        CASE
            WHEN f.FacultyKey LIKE N'ROSTER:%'
             AND rm.DepartmentRosterMemberId =
                 TRY_CONVERT(
                     BIGINT,
                     SUBSTRING(
                         f.FacultyKey,
                         LEN(N'ROSTER:') + 1,
                         50))
                THEN 0
            ELSE 1
        END,
        rm.DepartmentRosterMemberId
) roster
WHERE NULLIF(LTRIM(RTRIM(f.UvaUserId)), N'') IS NOT NULL
  AND
  (
      (
          f.PublicationIdentitySource = N'OpenAlex'
          AND UPPER(LTRIM(RTRIM(f.SourceAuthorId))) IN ({inClause})
      )
      OR
      (
          roster.OpenAlexResolutionStatus IN (N'ProposedUnique', N'DoiSeedConverged')
          AND UPPER(LTRIM(RTRIM(roster.OpenAlexAuthorId))) IN ({inClause})
      )
  )
ORDER BY UvaUserId;";

        var rows = new List<string>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                rows.Add(reader.GetString(0));
            }
        }

        return rows
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetNullableString(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        string objectName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0
    ELSE 1
END;";

        command.Parameters.AddWithValue(
            "@ObjectName",
            objectName);

        command.Parameters.AddWithValue(
            "@ObjectType",
            objectType);

        var value =
            await command.ExecuteScalarAsync(cancellationToken);

        return value is not null &&
               value != DBNull.Value &&
               Convert.ToInt32(value) == 1;
    }
}
