param(
    [Parameter(Mandatory = $true)]
    [string]$JobName
)

Set-Location "$PSScriptRoot\..\src\OpenAlex.Worker"

if (-not (Test-Path "appsettings.json")) {
    Copy-Item "appsettings.example.json" "appsettings.json"
    Write-Host "Created appsettings.json. Edit Worker:JobName to '$JobName' and Worker:Enabled to true before running again."
    exit 0
}

Write-Host "Make sure appsettings.json has:"
Write-Host '  "Worker": { "Enabled": true, "JobName": "'$JobName'", "RunOnceAndStop": true }'

dotnet clean
dotnet restore
dotnet build
dotnet run
