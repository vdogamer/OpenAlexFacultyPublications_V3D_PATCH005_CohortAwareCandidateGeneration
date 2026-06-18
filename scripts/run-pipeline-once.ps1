Set-Location "$PSScriptRoot\..\src\OpenAlex.Worker"

if (-not (Test-Path "appsettings.json")) {
    Copy-Item "appsettings.example.json" "appsettings.json"
    Write-Host "Created appsettings.json."
    Write-Host 'Set Worker:Enabled=true and Worker:JobName="RunPipelineOnce", then run this script again.'
    exit 0
}

Write-Host 'Make sure Worker:Enabled=true and Worker:JobName="RunPipelineOnce" in appsettings.json.'

dotnet clean
dotnet restore
dotnet build
dotnet run
