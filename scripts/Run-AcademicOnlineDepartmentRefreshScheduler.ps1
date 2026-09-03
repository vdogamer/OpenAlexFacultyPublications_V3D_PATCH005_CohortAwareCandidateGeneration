param(
    [int]$MaxDepartments = 1,

    [int]$LeaseMinutes = 180,

    [switch]$Continuous,

    [int]$DelaySeconds = 900
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:AcademicOnline__ApiKey)) {
    throw "AcademicOnline__ApiKey is not set. Run .\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1 first."
}

if ($MaxDepartments -lt 1 -or $MaxDepartments -gt 50) {
    throw "MaxDepartments must be between 1 and 50."
}

if ($LeaseMinutes -lt 5 -or $LeaseMinutes -gt 1440) {
    throw "LeaseMinutes must be between 5 and 1440."
}

if ($DelaySeconds -lt 10) {
    throw "DelaySeconds must be at least 10."
}

$env:Worker__Enabled = "true"
$env:Worker__JobName = "AcademicOnlineDepartmentRefreshScheduler"
$env:Worker__RunOnceAndStop =
    $(if ($Continuous.IsPresent) { "false" } else { "true" })
$env:Worker__DelaySeconds = $DelaySeconds.ToString()

$env:AcademicOnlineDepartmentRefreshScheduler__MaxDepartmentsPerPass =
    $MaxDepartments.ToString()
$env:AcademicOnlineDepartmentRefreshScheduler__LeaseMinutes =
    $LeaseMinutes.ToString()

Write-Host "Running PATCH020 Academic Online department refresh scheduler..."
Write-Host "Mode: $(if ($Continuous.IsPresent) { 'continuous' } else { 'one pass' })"
Write-Host "MaxDepartmentsPerPass: $MaxDepartments"
Write-Host "LeaseMinutes: $LeaseMinutes"

if ($Continuous.IsPresent) {
    Write-Host "Poll delay: $DelaySeconds seconds"
}

dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
