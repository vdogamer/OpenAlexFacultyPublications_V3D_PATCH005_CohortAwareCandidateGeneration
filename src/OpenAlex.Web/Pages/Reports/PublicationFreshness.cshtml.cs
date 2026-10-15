using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenAlex.Data;
using OpenAlex.Web.Auth;
using OpenAlex.Web.Services;

namespace OpenAlex.Web.Pages.Reports;

[Authorize(Policy = "ReaderPolicy")]
public sealed class PublicationFreshnessModel : PageModel
{
    private readonly FacultyPublicationFreshnessService _service;
    private readonly DepartmentReviewAuthorizationService _departmentReviewAuthorization;
    private readonly PublicationFreshnessAuditRepository _auditRepository;
    private readonly ILogger<PublicationFreshnessModel> _logger;

    public PublicationFreshnessModel(
        FacultyPublicationFreshnessService service,
        DepartmentReviewAuthorizationService departmentReviewAuthorization,
        PublicationFreshnessAuditRepository auditRepository,
        ILogger<PublicationFreshnessModel> logger)
    {
        _service = service;
        _departmentReviewAuthorization = departmentReviewAuthorization;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FromYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Doi { get; set; }

    public FacultyPublicationFreshnessResult? Result { get; private set; }

    public PublicationDoiLookupResult? DoiResult { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? DoiErrorMessage { get; private set; }

    public string? ReviewActionMessage { get; private set; }

    public string? ReviewActionErrorMessage { get; private set; }

    public ISet<long> AuthorizedReviewDepartmentIds { get; } =
        new HashSet<long>();

    public bool HasReviewerRole =>
        User.IsInRole("Reviewer") ||
        User.IsInRole("Admin");

    public bool CanCurrentUserSendToReview(
        PublicationDoiFacultyCandidate candidate)
    {
        return candidate.CanSendToReview &&
               candidate.ReviewDepartmentId.HasValue &&
               AuthorizedReviewDepartmentIds.Contains(
                   candidate.ReviewDepartmentId.Value);
    }

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        ReviewActionMessage =
            TempData["PublicationFreshnessReviewMessage"] as string;

        ReviewActionErrorMessage =
            TempData["PublicationFreshnessReviewError"] as string;

        if (!string.IsNullOrWhiteSpace(Doi))
        {
            await RunDoiScanAsync(cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(UserId))
        {
            return;
        }

        await RunUserIdScanAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSendToReviewAsync(
        string doi,
        string uvaUserId,
        long departmentId,
        CancellationToken cancellationToken)
    {
        if (!HasReviewerRole)
        {
            return Forbid();
        }

        if (!await _departmentReviewAuthorization
                .CanReviewDepartmentAsync(
                    User,
                    departmentId,
                    cancellationToken))
        {
            return Forbid();
        }

        try
        {
            var submittedBy = GetCurrentUserName();

            var result =
                await _service.SendDoiToReviewAsync(
                    doi,
                    uvaUserId,
                    departmentId,
                    submittedBy,
                    cancellationToken);

            TempData["PublicationFreshnessReviewMessage"] =
                result.WasCreated
                    ? $"Publication sent to review. Candidate #{result.FacultyPublicationReviewCandidateId} was created."
                    : $"This faculty/publication pair is already in the review workflow as candidate #{result.FacultyPublicationReviewCandidateId}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["PublicationFreshnessReviewError"] =
                ex.Message;
        }

        return RedirectToPage(
            new
            {
                Doi = doi
            });
    }

    private async Task RunUserIdScanAsync(
        CancellationToken cancellationToken)
    {
        var requestedValue = UserId!.Trim();
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Result =
                await _service.ScanByUserIdAsync(
                    requestedValue,
                    FromYear,
                    cancellationToken);

            stopwatch.Stop();

            UserId = Result.UvaUserId;
            FromYear = Result.FromYear;

            await RecordAuditBestEffortAsync(
                new PublicationFreshnessScanAuditWrite
                {
                    ScanType = "UserId",
                    RequestedValue = requestedValue,
                    NormalizedValue = Result.UvaUserId,
                    RequestedBy = GetCurrentUserName(),
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    Succeeded = true,
                    UvaUserId = Result.UvaUserId,
                    FacultyDisplayName = Result.FacultyName,
                    DepartmentNames =
                        string.Join("; ", Result.Departments),
                    FacultyMatchCount = 1,
                    LocalResultCount =
                        Result.LocalPublicationCount,
                    AcademicOnlineResultCount =
                        Result.AcademicOnlinePublicationCount,
                    OpenAlexResultCount =
                        Result.OpenAlexPublicationCount,
                    ComparedItemCount = Result.Items.Count,
                    AttentionItemCount = Result.AttentionCount,
                    AcademicOnlineWasAttempted =
                        Result.AcademicOnlineSource.WasAttempted,
                    AcademicOnlineIsAvailable =
                        Result.AcademicOnlineSource.IsAvailable,
                    OpenAlexWasAttempted =
                        Result.OpenAlexSource.WasAttempted,
                    OpenAlexIsAvailable =
                        Result.OpenAlexSource.IsAvailable,
                    Warnings = Result.Warnings
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            ErrorMessage = ex.Message;

            await RecordAuditBestEffortAsync(
                CreateFailedAudit(
                    "UserId",
                    requestedValue,
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    ex.Message),
                cancellationToken);
        }
    }

    private async Task RunDoiScanAsync(
        CancellationToken cancellationToken)
    {
        var requestedValue = Doi!.Trim();
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            DoiResult =
                await _service.LookupDoiAsync(
                    requestedValue,
                    cancellationToken);

            stopwatch.Stop();

            Doi = DoiResult.Doi;

            await LoadDoiReviewAuthorizationsAsync(
                cancellationToken);

            var warnings = DoiResult.Warnings
                .Concat(
                    DoiResult.FacultyCandidates
                        .SelectMany(candidate =>
                            candidate.ScanWarnings))
                .Concat(
                    DoiResult.FacultyCandidates
                        .Where(candidate =>
                            !string.IsNullOrWhiteSpace(
                                candidate.ScanError))
                        .Select(candidate =>
                            candidate.ScanError!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var singleCandidate =
                DoiResult.FacultyCandidates.Count == 1
                    ? DoiResult.FacultyCandidates[0]
                    : null;

            var departmentNames =
                DoiResult.FacultyCandidates
                    .SelectMany(candidate =>
                        candidate.Departments)
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToArray();

            var academicOnlineWasAttempted =
                DoiResult.FacultyCandidates.Any(candidate =>
                    candidate.AcademicOnlineWasAttempted);

            var academicOnlineIsAvailable =
                DoiResult.FacultyCandidates.Any(candidate =>
                    candidate.AcademicOnlineWasAttempted &&
                    candidate.AcademicOnlineIsAvailable);

            var attentionCount =
                DoiResult.FacultyCandidates.Count(candidate =>
                    !candidate.ExistsLocally ||
                    !string.IsNullOrWhiteSpace(candidate.ReviewStatus) ||
                    !string.IsNullOrWhiteSpace(candidate.ScanError));

            if (DoiResult.Publication is not null &&
                DoiResult.FacultyCandidates.Count == 0)
            {
                attentionCount = 1;
            }

            await RecordAuditBestEffortAsync(
                new PublicationFreshnessScanAuditWrite
                {
                    ScanType = "Doi",
                    RequestedValue = requestedValue,
                    NormalizedValue = DoiResult.Doi,
                    RequestedBy = GetCurrentUserName(),
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    Succeeded = true,
                    UvaUserId = singleCandidate?.UvaUserId,
                    FacultyDisplayName =
                        singleCandidate?.FacultyName,
                    DepartmentNames =
                        departmentNames.Length == 0
                            ? null
                            : string.Join(
                                "; ",
                                departmentNames),
                    DoiNormalized = DoiResult.Doi,
                    OpenAlexWorkId =
                        DoiResult.Publication?.OpenAlexWorkId,
                    FacultyMatchCount =
                        DoiResult.FacultyCandidates.Count,
                    LocalResultCount =
                        DoiResult.FacultyCandidates.Count(
                            candidate =>
                                candidate.ExistsLocally),
                    AcademicOnlineResultCount =
                        DoiResult.FacultyCandidates.Count(
                            candidate =>
                                candidate.ExistsInAcademicOnline),
                    OpenAlexResultCount =
                        DoiResult.Publication is null ? 0 : 1,
                    ComparedItemCount =
                        DoiResult.FacultyCandidates.Count,
                    AttentionItemCount = attentionCount,
                    AcademicOnlineWasAttempted =
                        academicOnlineWasAttempted,
                    AcademicOnlineIsAvailable =
                        academicOnlineIsAvailable,
                    OpenAlexWasAttempted =
                        DoiResult.OpenAlexWasAttempted,
                    OpenAlexIsAvailable =
                        DoiResult.OpenAlexIsAvailable,
                    Warnings = warnings
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            DoiErrorMessage = ex.Message;

            await RecordAuditBestEffortAsync(
                CreateFailedAudit(
                    "Doi",
                    requestedValue,
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    ex.Message),
                cancellationToken);
        }
    }

    private PublicationFreshnessScanAuditWrite CreateFailedAudit(
        string scanType,
        string requestedValue,
        DateTime startedAtUtc,
        long durationMs,
        string errorMessage)
    {
        return new PublicationFreshnessScanAuditWrite
        {
            ScanType = scanType,
            RequestedValue = requestedValue,
            RequestedBy = GetCurrentUserName(),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTime.UtcNow,
            DurationMs = durationMs,
            Succeeded = false,
            ErrorMessage = errorMessage,
            Warnings = Array.Empty<string>()
        };
    }

    private async Task RecordAuditBestEffortAsync(
        PublicationFreshnessScanAuditWrite audit,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditRepository.RecordScanAsync(
                audit,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Publication freshness scan completed, but its audit record could not be written.");
        }
    }

    private string GetCurrentUserName()
    {
        return User?.Identity?.IsAuthenticated == true &&
               !string.IsNullOrWhiteSpace(User.Identity.Name)
            ? User.Identity.Name!
            : "local.demo.reader";
    }

    private async Task LoadDoiReviewAuthorizationsAsync(
        CancellationToken cancellationToken)
    {
        if (DoiResult is null || !HasReviewerRole)
        {
            return;
        }

        var departmentIds = DoiResult.FacultyCandidates
            .Where(candidate =>
                candidate.CanSendToReview &&
                candidate.ReviewDepartmentId.HasValue)
            .Select(candidate =>
                candidate.ReviewDepartmentId!.Value)
            .Distinct()
            .ToArray();

        foreach (var departmentId in departmentIds)
        {
            if (await _departmentReviewAuthorization
                    .CanReviewDepartmentAsync(
                        User,
                        departmentId,
                        cancellationToken))
            {
                AuthorizedReviewDepartmentIds.Add(
                    departmentId);
            }
        }
    }
}
