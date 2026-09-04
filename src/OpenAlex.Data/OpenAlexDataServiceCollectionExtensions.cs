using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenAlex.Data;

public static class OpenAlexDataServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAlexData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.AddSingleton<SqlConnectionFactory>();

        services.AddScoped<DashboardRepository>();
        services.AddScoped<FacultyRepository>();
        services.AddScoped<ReviewQueueRepository>();
        services.AddScoped<IdentityReviewRepository>();
        services.AddScoped<ReportingRepository>();
        services.AddScoped<PublicationProvenanceRepository>();
        services.AddScoped<PublicationProvenanceCoverageRepository>();
        services.AddScoped<AcademicOnlineWorkBackfillPreviewRepository>();
        services.AddScoped<AcademicOnlineWorkBackfillRepository>();
        services.AddScoped<FacultyPublicationCandidatePreviewRepository>();
        services.AddScoped<FacultyAuthorIdentityResolutionPreviewRepository>();
        services.AddScoped<FacultyAuthorIdentityResolutionSourceRepository>();
        services.AddScoped<AcceptedIdentityBridgePreviewRepository>();
        services.AddScoped<AcceptedIdentityBridgeExecutionRepository>();
        services.AddScoped<OperationsRepository>();
        services.AddScoped<InstitutionIngestionRepository>();
        services.AddScoped<AuthorIngestionRepository>();
        services.AddScoped<WorkIngestionRepository>();
        services.AddScoped<CandidateGenerationRepository>();
        services.AddScoped<AcceptedIdentityPublicationBridgeRepository>();
        services.AddScoped<PublicationEvidenceEnhancementRepository>();
        services.AddScoped<AcademicOnlineComparisonRepository>();
        services.AddScoped<AcademicOnlineReportingStoreRepository>();
        services.AddScoped<AcademicOnlineDepartmentRefreshRepository>();
        services.AddScoped<AcademicOnlineDepartmentDiscoveryRepository>();
        services.AddScoped<FacultyManualReviewRuleRepository>();
        services.AddScoped<FacultyIdentityCandidateRepository>();
        services.AddScoped<FacultyIdentityCandidateWorkRepository>();
        services.AddScoped<FacultyIdentityWorkEvidenceScoreRepository>();
        services.AddScoped<FacultyIdentityCandidateSummaryRepository>();
        services.AddScoped<ReviewDecisionRepository>();
        services.AddScoped<AuthRepository>();
        services.AddScoped<JobDashboardRepository>();
        services.AddScoped<OperationalDashboardRepository>();
        services.AddScoped<SchedulerRepository>();
        services.AddScoped<RoleManagementRepository>();
        services.AddScoped<ExecutionWindowRepository>();
        services.AddScoped<ExternalGroupRoleMappingService>();

        return services;
    }
}
