[CmdletBinding()]
param(
    [string]$ServiceName = "OpenAlexAcademicOnlineDepartmentRefresh"
)

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    Write-Host "Service is not installed: $ServiceName"
    exit 1
}

$service |
    Select-Object Name, DisplayName, Status, StartType |
    Format-List

Write-Host "PATCH020A heartbeat remains the authoritative application-health check."
Write-Host "Run sql/420_V3E_PATCH020A_SchedulerHeartbeat_Diagnostics_SQL2016.sql"
Write-Host "or open /Admin/AcademicOnlineDepartments."
