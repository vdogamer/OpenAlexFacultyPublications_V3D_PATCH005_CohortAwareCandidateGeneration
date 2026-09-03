[CmdletBinding()]
param(
    [string]$ServiceName = "OpenAlexAcademicOnlineDepartmentRefresh",
    [string]$InstallPath = "$env:ProgramData\OpenAlex\AcademicOnlineDepartmentRefresh",
    [switch]$RemovePublishedFiles
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run PowerShell as Administrator before uninstalling the Windows service."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -ne $service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force

        $service.WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(30))
    }

    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Removed Windows service: $ServiceName"
}
else {
    Write-Host "Service is not installed: $ServiceName"
}

if ($RemovePublishedFiles.IsPresent -and (Test-Path $InstallPath)) {
    Remove-Item -Path $InstallPath -Recurse -Force

    Write-Host "Removed published worker files:"
    Write-Host "  $InstallPath"
}
