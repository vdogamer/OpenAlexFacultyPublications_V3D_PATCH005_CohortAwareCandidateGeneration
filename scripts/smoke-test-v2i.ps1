param(
    [string]$WebUrl = "http://localhost:5028"
)

Write-Host "OpenAlex V2I Smoke Test"
Write-Host "1. Build solution"
dotnet build "$PSScriptRoot\..\OpenAlexFacultyPublications.sln"

Write-Host "2. Expected manual checks:"
Write-Host "   - Open $WebUrl"
Write-Host "   - Login as admin.demo"
Write-Host "   - Open /Operations"
Write-Host "   - Open /Operations/Runs"
Write-Host "   - Open /Operations/Logs"
Write-Host "   - Open /Operations/Errors"
Write-Host "   - Open /Admin/Jobs"
Write-Host "   - Open /Reports/AcceptedPublications"

Write-Host "3. Run SQL scripts in SSMS:"
Write-Host "   :r sql/120_V2I_Production_Readiness_Report.sql"
Write-Host "   :r sql/121_V2I_Operational_Dashboard_Audit.sql"
Write-Host "   :r sql/122_V2I_Final_Smoke_Test_Report.sql"
