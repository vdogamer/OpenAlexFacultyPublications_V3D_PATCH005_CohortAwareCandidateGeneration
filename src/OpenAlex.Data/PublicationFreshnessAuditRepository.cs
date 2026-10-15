using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace OpenAlex.Data;

public sealed class PublicationFreshnessAuditRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public PublicationFreshnessAuditRepository(
        SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> RecordScanAsync(
        PublicationFreshnessScanAuditWrite audit,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO academiconline.FacultyPublicationFreshnessScanAudit
(
    ScanType,
    RequestedValue,
    NormalizedValue,
    RequestedBy,
    StartedAtUtc,
    CompletedAtUtc,
    DurationMs,
    Succeeded,
    ErrorMessage,
    UvaUserId,
    FacultyDisplayName,
    DepartmentNames,
    DoiNormalized,
    OpenAlexWorkId,
    FacultyMatchCount,
    LocalResultCount,
    AcademicOnlineResultCount,
    OpenAlexResultCount,
    ComparedItemCount,
    AttentionItemCount,
    WarningCount,
    AcademicOnlineWasAttempted,
    AcademicOnlineIsAvailable,
    OpenAlexWasAttempted,
    OpenAlexIsAvailable,
    WarningsJson
)
VALUES
(
    @ScanType,
    @RequestedValue,
    @NormalizedValue,
    @RequestedBy,
    @StartedAtUtc,
    @CompletedAtUtc,
    @DurationMs,
    @Succeeded,
    @ErrorMessage,
    @UvaUserId,
    @FacultyDisplayName,
    @DepartmentNames,
    @DoiNormalized,
    @OpenAlexWorkId,
    @FacultyMatchCount,
    @LocalResultCount,
    @AcademicOnlineResultCount,
    @OpenAlexResultCount,
    @ComparedItemCount,
    @AttentionItemCount,
    @WarningCount,
    @AcademicOnlineWasAttempted,
    @AcademicOnlineIsAvailable,
    @OpenAlexWasAttempted,
    @OpenAlexIsAvailable,
    @WarningsJson
);

SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

        AddNVarChar(command, "@ScanType", 20, audit.ScanType);
        AddNVarChar(command, "@RequestedValue", 1024, audit.RequestedValue);
        AddNVarChar(command, "@NormalizedValue", 1024, audit.NormalizedValue);
        AddNVarChar(command, "@RequestedBy", 256, audit.RequestedBy);
        command.Parameters.AddWithValue(
            "@StartedAtUtc",
            audit.StartedAtUtc);
        command.Parameters.AddWithValue(
            "@CompletedAtUtc",
            audit.CompletedAtUtc);
        command.Parameters.AddWithValue(
            "@DurationMs",
            audit.DurationMs);
        command.Parameters.AddWithValue(
            "@Succeeded",
            audit.Succeeded);
        AddNVarChar(command, "@ErrorMessage", -1, audit.ErrorMessage);
        AddNVarChar(command, "@UvaUserId", 1024, audit.UvaUserId);
        AddNVarChar(
            command,
            "@FacultyDisplayName",
            1024,
            audit.FacultyDisplayName);
        AddNVarChar(
            command,
            "@DepartmentNames",
            -1,
            audit.DepartmentNames);
        AddNVarChar(
            command,
            "@DoiNormalized",
            1024,
            audit.DoiNormalized);
        AddNVarChar(
            command,
            "@OpenAlexWorkId",
            256,
            audit.OpenAlexWorkId);
        command.Parameters.AddWithValue(
            "@FacultyMatchCount",
            audit.FacultyMatchCount);
        command.Parameters.AddWithValue(
            "@LocalResultCount",
            audit.LocalResultCount);
        command.Parameters.AddWithValue(
            "@AcademicOnlineResultCount",
            audit.AcademicOnlineResultCount);
        command.Parameters.AddWithValue(
            "@OpenAlexResultCount",
            audit.OpenAlexResultCount);
        command.Parameters.AddWithValue(
            "@ComparedItemCount",
            audit.ComparedItemCount);
        command.Parameters.AddWithValue(
            "@AttentionItemCount",
            audit.AttentionItemCount);
        command.Parameters.AddWithValue(
            "@WarningCount",
            audit.Warnings.Count);
        command.Parameters.AddWithValue(
            "@AcademicOnlineWasAttempted",
            audit.AcademicOnlineWasAttempted);
        command.Parameters.AddWithValue(
            "@AcademicOnlineIsAvailable",
            audit.AcademicOnlineIsAvailable);
        command.Parameters.AddWithValue(
            "@OpenAlexWasAttempted",
            audit.OpenAlexWasAttempted);
        command.Parameters.AddWithValue(
            "@OpenAlexIsAvailable",
            audit.OpenAlexIsAvailable);

        AddNVarChar(
            command,
            "@WarningsJson",
            -1,
            audit.Warnings.Count == 0
                ? null
                : JsonSerializer.Serialize(audit.Warnings));

        var result =
            await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<PublicationFreshnessScanAuditRow>>
        GetRecentScansAsync(
            int top = 200,
            string? scanType = null,
            string? uvaUserId = null,
            string? requestedBy = null,
            bool attentionOnly = false,
            CancellationToken cancellationToken = default)
    {
        await using var connection =
            _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP (@Top)
    FacultyPublicationFreshnessScanAuditId,
    ScanType,
    RequestedValue,
    NormalizedValue,
    RequestedBy,
    StartedAtUtc,
    CompletedAtUtc,
    DurationMs,
    Succeeded,
    ErrorMessage,
    UvaUserId,
    FacultyDisplayName,
    DepartmentNames,
    DoiNormalized,
    OpenAlexWorkId,
    FacultyMatchCount,
    LocalResultCount,
    AcademicOnlineResultCount,
    OpenAlexResultCount,
    ComparedItemCount,
    AttentionItemCount,
    WarningCount,
    AcademicOnlineWasAttempted,
    AcademicOnlineIsAvailable,
    OpenAlexWasAttempted,
    OpenAlexIsAvailable,
    WarningsJson
FROM academiconline.FacultyPublicationFreshnessScanAudit
WHERE
    (@ScanType IS NULL OR ScanType = @ScanType)
    AND (@UvaUserId IS NULL OR UvaUserId = @UvaUserId)
    AND (@RequestedBy IS NULL OR RequestedBy = @RequestedBy)
    AND (
        @AttentionOnly = 0
        OR Succeeded = 0
        OR AttentionItemCount > 0
        OR WarningCount > 0
    )
ORDER BY FacultyPublicationFreshnessScanAuditId DESC;";

        command.Parameters.AddWithValue(
            "@Top",
            Math.Clamp(top, 1, 1000));
        AddNVarChar(
            command,
            "@ScanType",
            20,
            NormalizeOptional(scanType));
        AddNVarChar(
            command,
            "@UvaUserId",
            1024,
            NormalizeOptional(uvaUserId));
        AddNVarChar(
            command,
            "@RequestedBy",
            256,
            NormalizeOptional(requestedBy));
        command.Parameters.AddWithValue(
            "@AttentionOnly",
            attentionOnly);

        var rows =
            new List<PublicationFreshnessScanAuditRow>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new PublicationFreshnessScanAuditRow
                {
                    ScanAuditId = reader.GetInt64(0),
                    ScanType = reader.GetString(1),
                    RequestedValue = reader.GetString(2),
                    NormalizedValue = GetNullableString(reader, 3),
                    RequestedBy = reader.GetString(4),
                    StartedAtUtc = reader.GetDateTime(5),
                    CompletedAtUtc = reader.GetDateTime(6),
                    DurationMs = reader.GetInt64(7),
                    Succeeded = reader.GetBoolean(8),
                    ErrorMessage = GetNullableString(reader, 9),
                    UvaUserId = GetNullableString(reader, 10),
                    FacultyDisplayName = GetNullableString(reader, 11),
                    DepartmentNames = GetNullableString(reader, 12),
                    DoiNormalized = GetNullableString(reader, 13),
                    OpenAlexWorkId = GetNullableString(reader, 14),
                    FacultyMatchCount = reader.GetInt32(15),
                    LocalResultCount = reader.GetInt32(16),
                    AcademicOnlineResultCount = reader.GetInt32(17),
                    OpenAlexResultCount = reader.GetInt32(18),
                    ComparedItemCount = reader.GetInt32(19),
                    AttentionItemCount = reader.GetInt32(20),
                    WarningCount = reader.GetInt32(21),
                    AcademicOnlineWasAttempted =
                        reader.GetBoolean(22),
                    AcademicOnlineIsAvailable =
                        reader.GetBoolean(23),
                    OpenAlexWasAttempted =
                        reader.GetBoolean(24),
                    OpenAlexIsAvailable =
                        reader.GetBoolean(25),
                    Warnings =
                        ParseWarnings(
                            GetNullableString(reader, 26))
                });
        }

        return rows;
    }

    private static IReadOnlyList<string> ParseWarnings(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ??
                   Array.Empty<string>();
        }
        catch (JsonException)
        {
            return new[] { json };
        }
    }

    private static string? GetNullableString(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void AddNVarChar(
        SqlCommand command,
        string name,
        int size,
        string? value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                System.Data.SqlDbType.NVarChar,
                size);

        parameter.Value =
            string.IsNullOrWhiteSpace(value)
                ? DBNull.Value
                : value.Trim();
    }
}

public sealed class PublicationFreshnessScanAuditWrite
{
    public string ScanType { get; init; } = string.Empty;
    public string RequestedValue { get; init; } = string.Empty;
    public string? NormalizedValue { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UvaUserId { get; init; }
    public string? FacultyDisplayName { get; init; }
    public string? DepartmentNames { get; init; }
    public string? DoiNormalized { get; init; }
    public string? OpenAlexWorkId { get; init; }
    public int FacultyMatchCount { get; init; }
    public int LocalResultCount { get; init; }
    public int AcademicOnlineResultCount { get; init; }
    public int OpenAlexResultCount { get; init; }
    public int ComparedItemCount { get; init; }
    public int AttentionItemCount { get; init; }
    public bool AcademicOnlineWasAttempted { get; init; }
    public bool AcademicOnlineIsAvailable { get; init; }
    public bool OpenAlexWasAttempted { get; init; }
    public bool OpenAlexIsAvailable { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } =
        Array.Empty<string>();
}

public sealed class PublicationFreshnessScanAuditRow
{
    public long ScanAuditId { get; init; }
    public string ScanType { get; init; } = string.Empty;
    public string RequestedValue { get; init; } = string.Empty;
    public string? NormalizedValue { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UvaUserId { get; init; }
    public string? FacultyDisplayName { get; init; }
    public string? DepartmentNames { get; init; }
    public string? DoiNormalized { get; init; }
    public string? OpenAlexWorkId { get; init; }
    public int FacultyMatchCount { get; init; }
    public int LocalResultCount { get; init; }
    public int AcademicOnlineResultCount { get; init; }
    public int OpenAlexResultCount { get; init; }
    public int ComparedItemCount { get; init; }
    public int AttentionItemCount { get; init; }
    public int WarningCount { get; init; }
    public bool AcademicOnlineWasAttempted { get; init; }
    public bool AcademicOnlineIsAvailable { get; init; }
    public bool OpenAlexWasAttempted { get; init; }
    public bool OpenAlexIsAvailable { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } =
        Array.Empty<string>();

    public string OutcomeLabel =>
        !Succeeded
            ? "Failed"
            : AttentionItemCount > 0 || WarningCount > 0
                ? "Attention"
                : "Clean";

    public string AcademicOnlineStatusLabel =>
        BuildSourceStatusLabel(
            AcademicOnlineWasAttempted,
            AcademicOnlineIsAvailable,
            AcademicOnlineResultCount);

    public string OpenAlexStatusLabel =>
        BuildSourceStatusLabel(
            OpenAlexWasAttempted,
            OpenAlexIsAvailable,
            OpenAlexResultCount);

    private static string BuildSourceStatusLabel(
        bool attempted,
        bool available,
        int count)
    {
        if (!attempted)
        {
            return "Not scanned";
        }

        if (!available)
        {
            return "Failed";
        }

        return $"Scanned ({count})";
    }
}
