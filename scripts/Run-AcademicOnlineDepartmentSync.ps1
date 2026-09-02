param(
    [Parameter(Mandatory = $true)]
    [string]$DepartmentName,

    [int]$FromYear = ((Get-Date).Year - 1),

    [Nullable[int]]$ToYear = $null,

    [int]$MaxScholars = 250,

    [switch]$VerifiedOnly
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DepartmentName) -or
    $DepartmentName -match 'PASTE EXACT DEPARTMENT NAME HERE|YOUR EXACT ACADEMIC ONLINE DEPARTMENT NAME|ACTUAL DEPARTMENT NAME') {
    throw "Provide the exact department name returned by Academic Online; do not use the placeholder text."
}

if ([string]::IsNullOrWhiteSpace($env:AcademicOnline__ApiKey)) {
    throw "AcademicOnline__ApiKey is not set. Run .\scripts\Set-AcademicOnlineApiKeyForCurrentSession.ps1 first."
}

$env:Worker__Enabled = "true"
$env:Worker__JobName = "AcademicOnlineDepartmentSync"
$env:Worker__RunOnceAndStop = "true"

$env:AcademicOnlineDepartmentSync__DepartmentName = $DepartmentName
$env:AcademicOnlineDepartmentSync__FromYear = $FromYear.ToString()
$env:AcademicOnlineDepartmentSync__MaxScholarsToProcess = $MaxScholars.ToString()
$env:AcademicOnlineDepartmentSync__VerifiedOnly =
    $(if ($VerifiedOnly.IsPresent) { "true" } else { "false" })

if ($null -eq $ToYear) {
    Remove-Item Env:AcademicOnlineDepartmentSync__ToYear -ErrorAction SilentlyContinue
}
else {
    $env:AcademicOnlineDepartmentSync__ToYear = $ToYear.Value.ToString()
}

Write-Host "Running PATCH019 Academic Online department sync..."
Write-Host "Department: $DepartmentName"
Write-Host "FromYear: $FromYear"
Write-Host "ToYear: $(if ($null -eq $ToYear) { '(current)' } else { $ToYear })"
Write-Host "MaxScholars: $MaxScholars"
Write-Host "VerifiedOnly: $($VerifiedOnly.IsPresent)"

dotnet run --project .\src\OpenAlex.Worker\OpenAlex.Worker.csproj
