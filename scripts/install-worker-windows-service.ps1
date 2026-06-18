param([string]$ServiceName = "OpenAlexFacultyPublicationsWorker", [string]$PublishPath = "C:\OpenAlexFacultyPublications\Worker", [string]$DisplayName = "OpenAlex Faculty Publications Worker")
$exePath = Join-Path $PublishPath "OpenAlex.Worker.exe"
if (-not (Test-Path $exePath)) { Write-Error "Worker executable not found: $exePath"; exit 1 }
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { Write-Host "Service already exists: $ServiceName"; exit 0 }
New-Service -Name $ServiceName -BinaryPathName "`"$exePath`"" -DisplayName $DisplayName -StartupType Manual -Description "Processes queued OpenAlex Faculty Publications jobs."
Write-Host "Installed service: $ServiceName"
