param([string]$ServiceName = "OpenAlexFacultyPublicationsWorker")
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host "Service not found: $ServiceName"; exit 0 }
if ($service.Status -ne "Stopped") { Stop-Service -Name $ServiceName -Force }
sc.exe delete $ServiceName
Write-Host "Deleted service: $ServiceName"
