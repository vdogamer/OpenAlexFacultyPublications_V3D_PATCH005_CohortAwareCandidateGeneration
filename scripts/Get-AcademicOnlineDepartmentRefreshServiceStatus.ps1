[CmdletBinding()]
param(
    [string]$ServiceName = "OpenAlexAcademicOnlineDepartmentRefresh"
)

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    Write-Host "Service is not installed: $ServiceName"
    exit 1
}

$serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"

$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$delayedAutoStart = $false

if (Test-Path $registryPath) {
    $registryValues = Get-ItemProperty -Path $registryPath
    $delayedAutoStart = ($registryValues.DelayedAutoStart -eq 1)
}

[pscustomobject]@{
    Name = $service.Name
    DisplayName = $service.DisplayName
    Status = $service.Status
    StartType = $service.StartType
    DelayedAutoStart = $delayedAutoStart
    ProcessId = $serviceInfo.ProcessId
    ServiceAccount = $serviceInfo.StartName
    ExecutablePath = $serviceInfo.PathName
} | Format-List

Write-Host ""
Write-Host "Windows Service recovery policy:"
& sc.exe qfailure $ServiceName

Write-Host ""
Write-Host "PATCH020A heartbeat remains the authoritative application-health check."
Write-Host "Run sql/420_V3E_PATCH020A_SchedulerHeartbeat_Diagnostics_SQL2016.sql"
Write-Host "or open /Admin/AcademicOnlineDepartments."
