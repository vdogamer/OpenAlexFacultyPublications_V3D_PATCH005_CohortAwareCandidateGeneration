using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;
using OpenAlex.Worker;
using OpenAlex.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

// PATCH017B8A security hardening: the default HttpClient logging pipeline includes
// the full request URI. OpenAlex API keys are query-string credentials, so suppress
// informational request/response URI logging for this named client.
builder.Logging.AddFilter("System.Net.Http.HttpClient.OpenAlexApiClient.LogicalHandler", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.OpenAlexApiClient.ClientHandler", LogLevel.Warning);

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.Configure<InstitutionIngestionOptions>(builder.Configuration.GetSection("InstitutionIngestion"));
builder.Services.Configure<AuthorIngestionOptions>(builder.Configuration.GetSection("AuthorIngestion"));
builder.Services.Configure<WorkIngestionOptions>(builder.Configuration.GetSection("WorkIngestion"));
builder.Services.Configure<CandidateGenerationOptions>(builder.Configuration.GetSection("CandidateGeneration"));
builder.Services.Configure<AcademicOnlineComparisonOptions>(builder.Configuration.GetSection("AcademicOnlineComparison"));
builder.Services.Configure<AcademicOnlineWorkBackfillPreviewOptions>(builder.Configuration.GetSection("AcademicOnlineWorkBackfillPreview"));
builder.Services.Configure<AcademicOnlineWorkBackfillOptions>(builder.Configuration.GetSection("AcademicOnlineWorkBackfill"));
builder.Services.Configure<FacultyPublicationCandidatePreviewOptions>(builder.Configuration.GetSection("FacultyPublicationCandidatePreview"));
builder.Services.Configure<FacultyPublicationCandidateExecutionOptions>(builder.Configuration.GetSection("FacultyPublicationCandidateExecution"));
builder.Services.Configure<FacultyAuthorIdentityResolutionPreviewOptions>(builder.Configuration.GetSection("FacultyAuthorIdentityResolutionPreview"));
builder.Services.Configure<AcceptedIdentityBridgePreviewOptions>(builder.Configuration.GetSection("AcceptedIdentityBridgePreview"));
builder.Services.Configure<AcceptedIdentityBridgeExecutionOptions>(builder.Configuration.GetSection("AcceptedIdentityBridgeExecution"));
builder.Services.Configure<AcceptedIdentityCohortBridgeExecutionOptions>(builder.Configuration.GetSection("AcceptedIdentityCohortBridgeExecution"));
builder.Services.Configure<AcceptedIdentityWorkPromotionOptions>(builder.Configuration.GetSection("AcceptedIdentityWorkPromotion"));
builder.Services.Configure<FacultyPublicationCohortExecutionOptions>(builder.Configuration.GetSection("FacultyPublicationCohortExecution"));
builder.Services.Configure<PipelineOrchestrationOptions>(builder.Configuration.GetSection("Pipeline"));
builder.Services.Configure<QueuedJobProcessorOptions>(builder.Configuration.GetSection("QueuedJobProcessor"));

builder.Services.AddOpenAlexData(builder.Configuration);
builder.Services.AddOpenAlexClient(builder.Configuration);
builder.Services.AddOpenAlexOperationalLogging(builder.Configuration);

builder.Services.AddScoped<FacultyPublicationCandidateExecutionRepository>();
builder.Services.AddScoped<AcceptedIdentityCohortBridgeExecutionRepository>();
builder.Services.AddScoped<AcceptedIdentityWorkPromotionRepository>();
builder.Services.AddScoped<FacultyPublicationCohortExecutionRepository>();

builder.Services.AddScoped<InstitutionIngestionPreviewJob>();
builder.Services.AddScoped<InstitutionIngestionJob>();
builder.Services.AddScoped<AuthorIngestionJob>();
builder.Services.AddScoped<WorkIngestionJob>();
builder.Services.AddScoped<FacultyIdentityCandidateGenerationJob>();
builder.Services.AddScoped<FacultyIdentityCandidateWorkExpansionJob>();
builder.Services.AddScoped<FacultyIdentityWorkEvidenceScoringJob>();
builder.Services.AddScoped<FacultyIdentityCandidateSummaryGenerationJob>();
builder.Services.AddScoped<FacultyAuthorCandidateGenerationJob>();
builder.Services.AddScoped<FacultyPublicationCandidateGenerationJob>();
builder.Services.AddScoped<AcademicOnlineComparisonJob>();
builder.Services.AddScoped<AcademicOnlineWorkBackfillPreviewJob>();
builder.Services.AddScoped<AcademicOnlineWorkBackfillJob>();
builder.Services.AddScoped<FacultyPublicationCandidatePreviewJob>();
builder.Services.AddScoped<FacultyPublicationCandidateExecutionJob>();
builder.Services.AddScoped<FacultyAuthorIdentityResolutionPreviewJob>();
builder.Services.AddScoped<AcceptedIdentityBridgePreviewJob>();
builder.Services.AddScoped<AcceptedIdentityBridgeExecutionJob>();
builder.Services.AddScoped<AcceptedIdentityCohortBridgeExecutionJob>();
builder.Services.AddScoped<AcceptedIdentityWorkPromotionJob>();
builder.Services.AddScoped<FacultyPublicationCohortExecutionJob>();
builder.Services.AddScoped<PipelineOrchestrationJob>();
builder.Services.AddScoped<QueuedJobRequestProcessorJob>();

builder.Services.AddHostedService<OpenAlexWorkerService>();

var host = builder.Build();
host.Run();
