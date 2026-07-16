using OpenAlex.Data;
using OpenAlex.OpenAlexClient;
using OpenAlex.OperationalLogging;
using OpenAlex.Worker;
using OpenAlex.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.Configure<InstitutionIngestionOptions>(builder.Configuration.GetSection("InstitutionIngestion"));
builder.Services.Configure<AuthorIngestionOptions>(builder.Configuration.GetSection("AuthorIngestion"));
builder.Services.Configure<WorkIngestionOptions>(builder.Configuration.GetSection("WorkIngestion"));
builder.Services.Configure<CandidateGenerationOptions>(builder.Configuration.GetSection("CandidateGeneration"));
builder.Services.Configure<AcademicOnlineComparisonOptions>(builder.Configuration.GetSection("AcademicOnlineComparison"));
builder.Services.Configure<AcademicOnlineWorkBackfillPreviewOptions>(builder.Configuration.GetSection("AcademicOnlineWorkBackfillPreview"));
builder.Services.Configure<PipelineOrchestrationOptions>(builder.Configuration.GetSection("Pipeline"));
builder.Services.Configure<QueuedJobProcessorOptions>(builder.Configuration.GetSection("QueuedJobProcessor"));

builder.Services.AddOpenAlexData(builder.Configuration);
builder.Services.AddOpenAlexClient(builder.Configuration);
builder.Services.AddOpenAlexOperationalLogging(builder.Configuration);

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
builder.Services.AddScoped<PipelineOrchestrationJob>();
builder.Services.AddScoped<QueuedJobRequestProcessorJob>();

builder.Services.AddHostedService<OpenAlexWorkerService>();

var host = builder.Build();
host.Run();
