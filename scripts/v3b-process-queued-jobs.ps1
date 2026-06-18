Set-Location "$PSScriptRoot\..\src\OpenAlex.Worker"
if (-not (Test-Path "appsettings.json")) { Copy-Item "appsettings.example.json" "appsettings.json"; Write-Host "Created appsettings.json." }
Write-Host 'Confirm appsettings.json has Worker:Enabled=true and Worker:JobName="ProcessQueuedJobRequests".'
dotnet clean
dotnet restore
dotnet build
dotnet run
