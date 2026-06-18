param([string]$JobName = "InstitutionIngestion")
Set-Location "$PSScriptRoot\..\src\OpenAlex.Worker"
if (-not (Test-Path "appsettings.json")) { Copy-Item "appsettings.example.json" "appsettings.json" }
Write-Host "Confirm Worker:Enabled=true, Worker:JobName=$JobName, Worker:RunOnceAndStop=true, and TestLoad limits are small."
dotnet clean
dotnet restore
dotnet build
dotnet run
