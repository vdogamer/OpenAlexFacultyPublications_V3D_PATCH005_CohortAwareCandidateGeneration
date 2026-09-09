param(
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

$repoRoot = (& git rev-parse --show-toplevel).Trim()

if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    throw 'This script must be run inside a Git repository.'
}

Set-Location $repoRoot

$tracked = @(& git ls-files)

$generated = @(
    $tracked |
    Where-Object {
        $_ -match '(^|/)(bin|obj)/'
    }
)

Write-Host ""
Write-Host "Tracked generated bin/obj files: $($generated.Count)"
Write-Host ""

if ($generated.Count -eq 0) {
    Write-Host "Repository hygiene is already clean."
    exit 0
}

$generated |
    Select-Object -First 25 |
    ForEach-Object {
        Write-Host "  $_"
    }

if ($generated.Count -gt 25) {
    Write-Host "  ... and $($generated.Count - 25) more"
}

Write-Host ""

if (-not $Apply) {
    Write-Host "Preview only. No Git index changes were made."
    Write-Host ""
    Write-Host "To untrack generated files without deleting local build output:"
    Write-Host "  .\scripts\Prepare-RepositoryHygieneCleanup.ps1 -Apply"
    exit 0
}

Write-Host "Removing generated bin/obj files from the Git index..."
Write-Host "Local files will remain on disk."

& git rm -r --cached --ignore-unmatch -- ':(glob)**/bin/**' ':(glob)**/obj/**'

if ($LASTEXITCODE -ne 0) {
    throw "git rm --cached failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Cleanup staged successfully."
Write-Host "Review before committing:"
Write-Host "  git status --short"
Write-Host ""
Write-Host "Recommended commit after review:"
Write-Host '  git commit -m "Stop tracking generated .NET build output"'
