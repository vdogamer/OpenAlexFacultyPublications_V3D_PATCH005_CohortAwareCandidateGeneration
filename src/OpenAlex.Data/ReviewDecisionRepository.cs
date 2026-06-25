using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class ReviewDecisionRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public ReviewDecisionRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AcceptAuthorCandidateAsync(long candidateId, string reviewedBy, string? notes, CancellationToken cancellationToken = default)
    {
        await SaveAuthorDecisionAsync(candidateId, "Accepted", "ManualReview", reviewedBy, notes, cancellationToken);
    }

    public async Task RejectAuthorCandidateAsync(long candidateId, string reviewedBy, string? notes, CancellationToken cancellationToken = default)
    {
        await SaveAuthorDecisionAsync(candidateId, "Rejected", "WrongPerson", reviewedBy, notes, cancellationToken);
    }

    public async Task AcceptPublicationCandidateAsync(long candidateId, string reviewedBy, string? notes, CancellationToken cancellationToken = default)
    {
        await SavePublicationDecisionAsync(candidateId, "Accepted", "ManualReview", reviewedBy, notes, cancellationToken);
    }

    public async Task RejectPublicationCandidateAsync(long candidateId, string reviewedBy, string? notes, CancellationToken cancellationToken = default)
    {
        await SavePublicationDecisionAsync(candidateId, "Rejected", "WrongFacultyAuthor", reviewedBy, notes, cancellationToken);
    }

    public async Task MarkPublicationCandidateAsNearDuplicateAsync(long candidateId, long relatedCandidateId, string reviewedBy, string? notes, CancellationToken cancellationToken = default)
    {
        await SaveNearDuplicateResolutionAsync(candidateId, relatedCandidateId, "DuplicateOfCandidate", reviewedBy, notes, cancellationToken);
    }

    public async Task KeepNearDuplicatePublicationCandidateSeparateAsync(long candidateId, string reviewedBy, string? notes, CancellationToken cancellationToken = default)
    {
        await SaveNearDuplicateResolutionAsync(candidateId, null, "KeepSeparate", reviewedBy, notes, cancellationToken);
    }

    public async Task SuppressNearDuplicatePublicationCandidateWarningAsync(long candidateId, string reviewedBy, string? notes, CancellationToken cancellationToken = default)
    {
        await SaveNearDuplicateResolutionAsync(candidateId, null, "SuppressWarning", reviewedBy, notes, cancellationToken);
    }

    private async Task SaveAuthorDecisionAsync(
        long candidateId,
        string decisionStatus,
        string decisionReason,
        string reviewedBy,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DECLARE
    @FacultyId BIGINT,
    @AuthorId BIGINT,
    @AuthorOpenAlexId VARCHAR(64),
    @AuthorDisplayName NVARCHAR(512),
    @AuthorOrcid NVARCHAR(256);

SELECT
    @FacultyId = FacultyId,
    @AuthorId = AuthorId,
    @AuthorOpenAlexId = AuthorOpenAlexId,
    @AuthorDisplayName = AuthorDisplayName,
    @AuthorOrcid = AuthorOrcid
FROM openalex.FacultyAuthorMatchCandidate
WHERE FacultyAuthorMatchCandidateId = @CandidateId;

IF @FacultyId IS NULL
BEGIN
    RAISERROR('Faculty author candidate was not found.', 16, 1);
    RETURN;
END;

UPDATE openalex.FacultyAuthorMatchDecision
SET
    IsCurrent = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND AuthorOpenAlexId = @AuthorOpenAlexId
  AND IsCurrent = 1;

INSERT INTO openalex.FacultyAuthorMatchDecision
(
    FacultyAuthorMatchCandidateId,
    FacultyId,
    AuthorId,
    AuthorOpenAlexId,
    AuthorDisplayName,
    AuthorOrcid,
    DecisionStatus,
    DecisionReason,
    DecisionSource,
    IsCurrent,
    ReviewedBy,
    ReviewedAtUtc,
    ReviewerNotes,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @CandidateId,
    @FacultyId,
    @AuthorId,
    @AuthorOpenAlexId,
    @AuthorDisplayName,
    @AuthorOrcid,
    @DecisionStatus,
    @DecisionReason,
    'HumanReviewer',
    1,
    @ReviewedBy,
    SYSUTCDATETIME(),
    @ReviewerNotes,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

UPDATE openalex.FacultyAuthorMatchCandidate
SET
    CandidateStatus = CASE WHEN @DecisionStatus = 'Accepted' THEN 'AutoAcceptedCandidate' ELSE 'AutoRejectedCandidate' END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyAuthorMatchCandidateId = @CandidateId;";

            command.Parameters.AddWithValue("@CandidateId", candidateId);
            command.Parameters.AddWithValue("@DecisionStatus", decisionStatus);
            command.Parameters.AddWithValue("@DecisionReason", decisionReason);
            command.Parameters.AddWithValue("@ReviewedBy", reviewedBy);
            command.Parameters.AddWithValue("@ReviewerNotes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task SavePublicationDecisionAsync(
        long candidateId,
        string decisionStatus,
        string decisionReason,
        string reviewedBy,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DECLARE
    @FacultyId BIGINT,
    @WorkId BIGINT,
    @WorkOpenAlexId VARCHAR(64),
    @WorkTitle NVARCHAR(MAX),
    @WorkDoi NVARCHAR(512),
    @PublicationYear INT;

SELECT
    @FacultyId = FacultyId,
    @WorkId = WorkId,
    @WorkOpenAlexId = WorkOpenAlexId,
    @WorkTitle = WorkTitle,
    @WorkDoi = WorkDoi,
    @PublicationYear = PublicationYear
FROM openalex.FacultyPublicationCandidate
WHERE FacultyPublicationCandidateId = @CandidateId;

IF @FacultyId IS NULL
BEGIN
    RAISERROR('Faculty publication candidate was not found.', 16, 1);
    RETURN;
END;

UPDATE openalex.FacultyPublicationDecision
SET
    IsCurrent = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyId = @FacultyId
  AND WorkId = @WorkId
  AND IsCurrent = 1;

INSERT INTO openalex.FacultyPublicationDecision
(
    FacultyPublicationCandidateId,
    FacultyId,
    WorkId,
    WorkOpenAlexId,
    WorkTitle,
    WorkDoi,
    PublicationYear,
    DecisionStatus,
    DecisionReason,
    DecisionSource,
    IsCurrent,
    ReviewedBy,
    ReviewedAtUtc,
    ReviewerNotes,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @CandidateId,
    @FacultyId,
    @WorkId,
    @WorkOpenAlexId,
    @WorkTitle,
    @WorkDoi,
    @PublicationYear,
    @DecisionStatus,
    @DecisionReason,
    'HumanReviewer',
    1,
    @ReviewedBy,
    SYSUTCDATETIME(),
    @ReviewerNotes,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

UPDATE openalex.FacultyPublicationCandidate
SET
    CandidateStatus = CASE WHEN @DecisionStatus = 'Accepted' THEN 'AutoAcceptedCandidate' ELSE 'AutoRejectedCandidate' END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCandidateId = @CandidateId;";

            command.Parameters.AddWithValue("@CandidateId", candidateId);
            command.Parameters.AddWithValue("@DecisionStatus", decisionStatus);
            command.Parameters.AddWithValue("@DecisionReason", decisionReason);
            command.Parameters.AddWithValue("@ReviewedBy", reviewedBy);
            command.Parameters.AddWithValue("@ReviewerNotes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task SaveNearDuplicateResolutionAsync(
        long candidateId,
        long? relatedCandidateId,
        string resolutionStatus,
        string reviewedBy,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
IF OBJECT_ID(N'openalex.FacultyPublicationNearDuplicateResolution', N'U') IS NULL
BEGIN
    RAISERROR('Near-duplicate resolution table does not exist. Run sql/310_V3E_PATCH012_NearDuplicateResolution_SQL2016.sql first.', 16, 1);
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM openalex.FacultyPublicationCandidate
    WHERE FacultyPublicationCandidateId = @CandidateId
)
BEGIN
    RAISERROR('Faculty publication candidate was not found.', 16, 1);
    RETURN;
END;

IF @RelatedCandidateId IS NOT NULL
BEGIN
    IF @RelatedCandidateId = @CandidateId
    BEGIN
        RAISERROR('Related candidate cannot be the same as the current candidate.', 16, 1);
        RETURN;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM openalex.FacultyPublicationCandidate
        WHERE FacultyPublicationCandidateId = @RelatedCandidateId
    )
    BEGIN
        RAISERROR('Related faculty publication candidate was not found.', 16, 1);
        RETURN;
    END;
END;

UPDATE openalex.FacultyPublicationNearDuplicateResolution
SET
    IsCurrent = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE FacultyPublicationCandidateId = @CandidateId
  AND IsCurrent = 1;

INSERT INTO openalex.FacultyPublicationNearDuplicateResolution
(
    FacultyPublicationCandidateId,
    RelatedFacultyPublicationCandidateId,
    ResolutionStatus,
    ResolutionReason,
    ReviewerName,
    IsCurrent,
    CreatedAtUtc,
    UpdatedAtUtc
)
VALUES
(
    @CandidateId,
    @RelatedCandidateId,
    @ResolutionStatus,
    @ResolutionReason,
    @ReviewerName,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);";

            command.Parameters.AddWithValue("@CandidateId", candidateId);
            command.Parameters.AddWithValue("@RelatedCandidateId", relatedCandidateId.HasValue ? (object)relatedCandidateId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@ResolutionStatus", resolutionStatus);
            command.Parameters.AddWithValue("@ResolutionReason", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes);
            command.Parameters.AddWithValue("@ReviewerName", reviewedBy);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
