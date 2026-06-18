param([string]$OutputPath = "C:\OpenAlexFacultyPublications\Worker")
Set-Location "$PSScriptRoot\..\src\OpenAlex.Worker"
dotnet publish -c Release -o $OutputPath
Write-Host "Published worker to: $OutputPath"
Write-Host "Copy config/appsettings.worker.service.template.json to appsettings.json and edit values before installing service."
