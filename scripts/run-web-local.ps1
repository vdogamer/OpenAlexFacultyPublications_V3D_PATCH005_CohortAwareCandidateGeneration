param(
    [string]$Url = "http://localhost:5027"
)

Set-Location "$PSScriptRoot\..\src\OpenAlex.Web"

if (-not (Test-Path "appsettings.json")) {
    Copy-Item "appsettings.example.json" "appsettings.json"
}

dotnet clean
dotnet restore
dotnet build

$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --urls $Url
