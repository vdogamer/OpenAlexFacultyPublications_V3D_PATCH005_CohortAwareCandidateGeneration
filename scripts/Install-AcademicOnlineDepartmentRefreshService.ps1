[CmdletBinding()]
param(
    [string]$ServiceName = "OpenAlexAcademicOnlineDepartmentRefresh",
    [string]$DisplayName = "OpenAlex Academic Online Department Refresh",
    [string]$InstallPath = "$env:ProgramData\OpenAlex\AcademicOnlineDepartmentRefresh",
    [ValidateSet("CurrentUser", "LocalSystem")]
    [string]$ServiceIdentity = "CurrentUser",
    [int]$PollSeconds = 900,
    [int]$MaxDepartmentsPerPass = 1,
    [int]$LeaseMinutes = 180,
    [string]$ConnectionString = "",
    [switch]$StartAfterInstall
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run PowerShell as Administrator before installing the Windows service."
    }
}

function Get-AcademicOnlineApiKey {
    if (-not [string]::IsNullOrWhiteSpace($env:AcademicOnline__ApiKey)) {
        return $env:AcademicOnline__ApiKey
    }

    $secureApiKey = Read-Host "Paste the Academic Online API key" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureApiKey)

    try {
        $plainApiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)

        if ([string]::IsNullOrWhiteSpace($plainApiKey)) {
            throw "Academic Online API key was empty."
        }

        return $plainApiKey
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }

        $secureApiKey = $null
    }
}

Assert-Administrator

if ($PollSeconds -lt 60 -or $PollSeconds -gt 86400) {
    throw "PollSeconds must be between 60 and 86400."
}

if ($MaxDepartmentsPerPass -lt 1 -or $MaxDepartmentsPerPass -gt 50) {
    throw "MaxDepartmentsPerPass must be between 1 and 50."
}

if ($LeaseMinutes -lt 5 -or $LeaseMinutes -gt 1440) {
    throw "LeaseMinutes must be between 5 and 1440."
}

$apiKey = Get-AcademicOnlineApiKey
$credential = $null

try {
    if ($ServiceIdentity -eq "CurrentUser") {
        if ([string]::IsNullOrWhiteSpace($env:USERDOMAIN)) {
            $defaultUser = $env:USERNAME
        }
        else {
            $defaultUser = "$env:USERDOMAIN\$env:USERNAME"
        }

        Write-Host ""
        Write-Host "The worker database uses Windows integrated authentication."
        Write-Host "Enter credentials for the Windows account that should run the service."
        Write-Host "For local validation, use the account that already accesses OpenAlexIngestion_Test."

        $credential = Get-Credential -UserName $defaultUser -Message "Windows service logon account"
    }

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($null -ne $existing) {
        Write-Host "Existing service found. Stopping and replacing it..."

        if ($existing.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName -Force
            $existing.WaitForStatus(
                [System.ServiceProcess.ServiceControllerStatus]::Stopped,
                [TimeSpan]::FromSeconds(30))
        }

        & sc.exe delete $ServiceName | Out-Null

        $deadline = (Get-Date).AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 500
            $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        }
        while ($null -ne $existing -and (Get-Date) -lt $deadline)

        if ($null -ne $existing) {
            throw "Windows did not remove the previous service within 30 seconds."
        }
    }

    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

    Write-Host "Publishing OpenAlex.Worker to:"
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

    $binaryPath = '"' + $serviceExe + '"'

    $newServiceParams = @{
        Name = $ServiceName
        BinaryPathName = $binaryPath
        DisplayName = $DisplayName
        Description = "Continuously polls the local Academic Online department catalog and refreshes only departments that are due."
        StartupType = "Automatic"
    }

    if ($ServiceIdentity -eq "CurrentUser") {
        $newServiceParams.Credential = $credential
    }

    New-Service @newServiceParams | Out-Null

    & sc.exe config $ServiceName start= delayed-auto | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
    & sc.exe failureflag $ServiceName 1 | Out-Null

    $serviceEnvironment = @(
        "DOTNET_ENVIRONMENT=Production",
        "Worker__Enabled=true",
        "Worker__JobName=AcademicOnlineDepartmentRefreshScheduler",
        "Worker__RunOnceAndStop=false",
        "Worker__DelaySeconds=$PollSeconds",
        "AcademicOnlineDepartmentRefreshScheduler__SchedulerKey=AcademicOnlineDepartmentRefreshScheduler",
        "AcademicOnlineDepartmentRefreshScheduler__MaxDepartmentsPerPass=$MaxDepartmentsPerPass",
        "AcademicOnlineDepartmentRefreshScheduler__LeaseMinutes=$LeaseMinutes",
        "AcademicOnlineDepartmentRefreshScheduler__StartedBy=OpenAlex.V3E.PATCH020B.WindowsService",
        "AcademicOnlineDepartmentRefreshScheduler__ApplicationVersion=3E-PATCH020B-DurableWorkerHosting",
        "AcademicOnline__ApiKey=$apiKey",
        "OperationalLogging__SourceApplication=OpenAlex.V3E.PATCH020B.Service",
        "OperationalLogging__EnvironmentName=WindowsService",
        "OperationalLogging__EnableDatabaseLogging=true"
    )

    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $serviceEnvironment += "ConnectionStrings__DefaultConnection=$ConnectionString"
        $serviceEnvironment += "Database__ConnectionString=$ConnectionString"
    }

    $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"

    New-ItemProperty -Path $registryPath -Name Environment -PropertyType MultiString -Value $serviceEnvironment -Force | Out-Null

    Write-Host ""
    Write-Host "Installed service: $ServiceName"
    Write-Host "Startup: Automatic (Delayed Start)"
    Write-Host "Poll interval: $PollSeconds seconds"
    Write-Host "Max departments/pass: $MaxDepartmentsPerPass"
    Write-Host "Lease: $LeaseMinutes minutes"
    Write-Host ""
    Write-Host "The Academic Online API key is stored in the service-specific HKLM environment block."
    Write-Host "It is not stored in the repository or service command line."

    if ($StartAfterInstall.IsPresent) {
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
}
finally {
    $apiKey = $null
    $credential = $null
}
