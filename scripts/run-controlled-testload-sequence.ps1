param(
    [string]$WorkerProjectPath = ".\src\OpenAlex.Worker"
)

Write-Host "Controlled OpenAlex TestLoad sequence"
Write-Host "Before running:"
Write-Host "1. Set OpenAlex:ApiKey in src\OpenAlex.Worker\appsettings.json"
Write-Host "2. Confirm TestLoad limits are small"
Write-Host "3. Run sql\170_TestLoad_Preflight_OpenAlex.sql in SSMS"
Write-Host ""
Write-Host "Recommended job order:"
Write-Host "  InstitutionIngestion"
Write-Host "  AuthorIngestion"
Write-Host "  WorkIngestion"
Write-Host "  FacultyAuthorCandidateGeneration"
Write-Host "  FacultyPublicationCandidateGeneration"
Write-Host ""
Write-Host "Run one job at a time by setting Worker:JobName in appsettings.json."
