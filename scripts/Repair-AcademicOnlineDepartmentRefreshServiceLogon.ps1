[CmdletBinding()]
param(
    [string]$ServiceName = "OpenAlexAcademicOnlineDepartmentRefresh",
    [switch]$StartService
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run PowerShell as Administrator before repairing the Windows service."
}

$serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"

if ($null -eq $serviceInfo) {
    throw "Windows service is not installed: $ServiceName"
}

$accountName = $serviceInfo.StartName

if ([string]::IsNullOrWhiteSpace($accountName)) {
    throw "The Windows service account could not be determined."
}

if ($accountName -eq "LocalSystem" -or $accountName -eq "NT AUTHORITY\SYSTEM") {
    Write-Host "The service runs as LocalSystem; no SeServiceLogonRight grant is required."
}
else {
    & "$PSScriptRoot\Grant-LogOnAsServiceRight.ps1" -AccountName $accountName
}

if ($StartService.IsPresent) {
    Start-Service -Name $ServiceName

    $service = Get-Service -Name $ServiceName

    $service.WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(30))

    Write-Host "Service status: Running"
}
else {
    Write-Host ""
    Write-Host "Start when ready:"
    Write-Host "  Start-Service -Name '$ServiceName'"
}
