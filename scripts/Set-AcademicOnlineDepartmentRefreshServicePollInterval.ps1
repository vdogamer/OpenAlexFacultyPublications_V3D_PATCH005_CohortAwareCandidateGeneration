[CmdletBinding()]
param(
    [string]$ServiceName = "OpenAlexAcademicOnlineDepartmentRefresh",
    [int]$PollSeconds = 900,
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run PowerShell as Administrator before changing the Windows service polling interval."
}

if ($PollSeconds -lt 60 -or $PollSeconds -gt 86400) {
    throw "PollSeconds must be between 60 and 86400."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    throw "Windows service is not installed: $ServiceName"
}

$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"

if (-not (Test-Path $registryPath)) {
    throw "Windows service registry configuration was not found: $registryPath"
}

$registryValues = Get-ItemProperty -Path $registryPath
$environmentValues = @($registryValues.Environment)

if ($environmentValues.Count -eq 0) {
    throw "The Windows service does not have a service-specific Environment block."
}

$delayPrefix = "Worker__DelaySeconds="
$replacement = "Worker__DelaySeconds=$PollSeconds"
$replaced = $false
$updatedEnvironment = @()

foreach ($entry in $environmentValues) {
    if ($entry -like "$delayPrefix*") {
        $updatedEnvironment += $replacement
        $replaced = $true
    }
    else {
        $updatedEnvironment += $entry
    }
}

if (-not $replaced) {
    $updatedEnvironment += $replacement
}

New-ItemProperty -Path $registryPath -Name Environment -PropertyType MultiString -Value $updatedEnvironment -Force | Out-Null

Write-Host "Updated polling interval for:"
Write-Host "  $ServiceName"
Write-Host "New poll interval:"
Write-Host "  $PollSeconds seconds"

if ($NoRestart.IsPresent) {
    Write-Host ""
    Write-Host "The new interval will take effect the next time the service starts."
    exit 0
}

if ($service.Status -eq "Running") {
    Restart-Service -Name $ServiceName -Force
}
else {
    Start-Service -Name $ServiceName
}

$service = Get-Service -Name $ServiceName

$service.WaitForStatus(
    [System.ServiceProcess.ServiceControllerStatus]::Running,
    [TimeSpan]::FromSeconds(30))

Write-Host "Service status: Running"
Write-Host ""
Write-Host "PATCH020A will record the new poll interval on the next scheduler pass."
