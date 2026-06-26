using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class ReportingRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public ReportingRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AcceptedPublicationDataRow>> GetAcceptedPublicationsAsync(int top = 500, CancellationToken cancellationToken = default)
    {
        var rows = new List<AcceptedPublicationDataRow>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyPublicationDecision", "U", cancellationToken))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT TOP ({Math.Clamp(top, 1, 5000)})
    d.FacultyPublicationCandidateId,
    f.DisplayName AS FacultyDisplayName,
    f.PrimaryEmail,
    f.DepartmentName,
    f.DivisionName,
    f.AppointmentTitle,
    COALESCE(pc.AuthorDisplayName, NULL) AS AuthorDisplayName,
    COALESCE(pc.AuthorOpenAlexId, NULL) AS AuthorOpenAlexId,
    COALESCE(pc.AuthorOrcid, NULL) AS AuthorOrcid,
    COALESCE(d.WorkTitle, pc.WorkTitle) AS WorkTitle,
    COALESCE(d.PublicationYear, pc.PublicationYear) AS PublicationYear,
    pc.PublicationDate,
    COALESCE(d.WorkDoi, pc.WorkDoi) AS WorkDoi,
    CASE
        WHEN COALESCE(d.WorkDoi, pc.WorkDoi) IS NULL THEN NULL
        WHEN COALESCE(d.WorkDoi, pc.WorkDoi) LIKE 'http%' THEN COALESCE(d.WorkDoi, pc.WorkDoi)
        ELSE 'https://doi.org/' + REPLACE(REPLACE(COALESCE(d.WorkDoi, pc.WorkDoi), 'https://doi.org/', ''), 'http://doi.org/', '')
    END AS DoiUrl,
    COALESCE(d.WorkOpenAlexId, pc.WorkOpenAlexId) AS WorkOpenAlexId,
    pc.WorkType,
    w.CitedByCount,
    d.DecisionStatus,
    d.ReviewedBy,
    d.ReviewedAtUtc,
    d.ReviewerNotes,
    pc.MatchScore,
    pc.EvidenceSummary,
    res.ResolutionStatus AS NearDuplicateResolutionStatus,
    res.RelatedFacultyPublicationCandidateId,
    res.ResolutionReason AS NearDuplicateResolutionReason
FROM openalex.FacultyPublicationDecision d
JOIN openalex.Faculty f
    ON d.FacultyId = f.FacultyId
LEFT JOIN openalex.FacultyPublicationCandidate pc
    ON d.FacultyPublicationCandidateId = pc.FacultyPublicationCandidateId
LEFT JOIN openalex.Work w
    ON COALESCE(d.WorkId, pc.WorkId) = w.WorkId
LEFT JOIN openalex.FacultyPublicationNearDuplicateResolution res
    ON res.FacultyPublicationCandidateId = d.FacultyPublicationCandidateId
   AND res.IsCurrent = 1
WHERE d.IsCurrent = 1
  AND d.DecisionStatus = 'Accepted'
ORDER BY f.DisplayName, COALESCE(d.PublicationYear, pc.PublicationYear) DESC, COALESCE(d.WorkTitle, pc.WorkTitle);";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AcceptedPublicationDataRow
            {
                FacultyPublicationCandidateId = reader.IsDBNull(0) ? null : reader.GetInt64(0),
                FacultyDisplayName = reader.GetString(1),
                PrimaryEmail = reader.IsDBNull(2) ? null : reader.GetString(2),
                DepartmentName = reader.IsDBNull(3) ? null : reader.GetString(3),
                DivisionName = reader.IsDBNull(4) ? null : reader.GetString(4),
                AppointmentTitle = reader.IsDBNull(5) ? null : reader.GetString(5),
                AuthorDisplayName = reader.IsDBNull(6) ? null : reader.GetString(6),
                AuthorOpenAlexId = reader.IsDBNull(7) ? null : reader.GetString(7),
                AuthorOrcid = reader.IsDBNull(8) ? null : reader.GetString(8),
                WorkTitle = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                PublicationYear = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                PublicationDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                WorkDoi = reader.IsDBNull(12) ? null : reader.GetString(12),
                DoiUrl = reader.IsDBNull(13) ? null : reader.GetString(13),
                WorkOpenAlexId = reader.IsDBNull(14) ? null : reader.GetString(14),
                WorkType = reader.IsDBNull(15) ? null : reader.GetString(15),
                CitedByCount = reader.IsDBNull(16) ? null : reader.GetInt32(16),
                DecisionStatus = reader.IsDBNull(17) ? null : reader.GetString(17),
                ReviewedBy = reader.IsDBNull(18) ? null : reader.GetString(18),
                ReviewedAtUtc = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                ReviewerNotes = reader.IsDBNull(20) ? null : reader.GetString(20),
                MatchScore = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                EvidenceSummary = reader.IsDBNull(22) ? null : reader.GetString(22),
                NearDuplicateResolutionStatus = reader.IsDBNull(23) ? null : reader.GetString(23),
                RelatedFacultyPublicationCandidateId = reader.IsDBNull(24) ? null : reader.GetInt64(24),
                NearDuplicateResolutionReason = reader.IsDBNull(25) ? null : reader.GetString(25)
            });
        }

        return rows;
    }

    private static async Task<bool> ObjectExistsAsync(Microsoft.Data.SqlClient.SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
