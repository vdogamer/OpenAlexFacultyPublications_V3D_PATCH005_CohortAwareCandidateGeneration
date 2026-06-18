param([string]$WebUrl = "http://localhost:5029")
Set-Location "$PSScriptRoot\.."
Write-Host "=== OpenAlex V3A Smoke Test ==="
dotnet clean
dotnet restore
dotnet build
dotnet test tests\OpenAlex.Tests\OpenAlex.Tests.csproj
Write-Host "Manual checks: $WebUrl, /Admin/Preflight, /Operations, /Admin/Jobs, /Reports/AcceptedPublications"
Write-Host "SQL: :r sql/130_V3A_Production_Preflight.sql; :r sql/131_V3A_Release_Verification.sql; :r sql/132_V3A_Test_Data_Quality_Report.sql"
