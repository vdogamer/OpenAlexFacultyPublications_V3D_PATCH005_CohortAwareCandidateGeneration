[CmdletBinding()]
param(
    [string]$ServiceName = "OpenAlexAcademicOnlineDepartmentRefresh",
    [string]$InstallPath = "$env:ProgramData\OpenAlex\AcademicOnlineDepartmentRefresh"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run PowerShell as Administrator before updating the Windows service binaries."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    throw "Windows service is not installed: $ServiceName"
}

$wasRunning = $service.Status -eq "Running"

if ($wasRunning) {
    Write-Host "Stopping service..."
    Stop-Service -Name $ServiceName -Force

    $service.WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Stopped,
        [TimeSpan]::FromSeconds(30))
}

try {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

    Write-Host "Publishing updated OpenAlex.Worker binaries to:"
    Write-Host "  $InstallPath"

    $publishArgs = @(
        "publish",
        ".\src\OpenAlex.Worker\OpenAlex.Worker.csproj",
        "-c",
        "Release",
        "--nologo",
        "-o",
        $InstallPath
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $serviceExe = Join-Path $InstallPath "OpenAlex.Worker.exe"

    if (-not (Test-Path $serviceExe)) {
        throw "Published worker executable was not found: $serviceExe"
    }
}
finally {
    if ($wasRunning) {
        Write-Host "Starting service..."
        Start-Service -Name $ServiceName

        $service = Get-Service -Name $ServiceName

        $service.WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(30))

        Write-Host "Service status: Running"
    }
}

Write-Host ""
Write-Host "Service environment, stored API key, service account, startup policy,"
Write-Host "and recovery policy were not modified."
