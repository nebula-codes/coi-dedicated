param(
    [ValidateRange(1024,65535)][int]$Port = 28560,
    [string]$DataDir,
    [switch]$NoBrowser,
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $DataDir) { $DataDir = Join-Path $projectRoot 'manager-data' }
$DataDir = [IO.Path]::GetFullPath($DataDir)
$url = "http://127.0.0.1:$Port"
$existing = $null
try { $existing = Invoke-RestMethod "$url/api/health" -TimeoutSec 2 } catch { }
if ($existing -and $existing.application -eq 'coi-dedicated-manager') {
    if ($Build) { throw 'The manager is already running. Close its process before rebuilding.' }
    if (-not $NoBrowser) { Start-Process $url }
    Write-Host "Server manager is already available at $url"
    return
}
$managerExe = Join-Path $projectRoot 'src\CoiDedicated.Manager\bin\Release\net10.0\CoiDedicated.Manager.exe'
$hostDll = Join-Path $projectRoot 'artifacts\manager-host\CoiDedicated.dll'
if ($Build -or -not (Test-Path -LiteralPath $hostDll)) {
    & dotnet build (Join-Path $projectRoot 'src\CoiDedicated\CoiDedicated.csproj') -c Release -o (Split-Path $hostDll -Parent)
    if ($LASTEXITCODE -ne 0) { throw 'Dedicated host build failed. Stop managed servers before rebuilding.' }
}
if ($Build -or -not (Test-Path -LiteralPath $managerExe)) {
    & dotnet build (Join-Path $projectRoot 'src\CoiDedicated.Manager\CoiDedicated.Manager.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Manager build failed.' }
}
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
# Start-Process joins arguments on Windows, so explicitly quote each path with spaces.
$managerArguments = @('--repository', ('"' + $projectRoot + '"'), '--data-dir', ('"' + $DataDir + '"'), '--host-dll', ('"' + $hostDll + '"'), '--port', "$Port")
$launched = Start-Process -FilePath $managerExe -ArgumentList $managerArguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $DataDir 'manager.log') -RedirectStandardError (Join-Path $DataDir 'manager-errors.log')
$ready = $false
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    if ($launched.HasExited) { throw "The manager could not start. See $DataDir\manager-errors.log" }
    try { $health = Invoke-RestMethod "$url/api/health" -TimeoutSec 1; $ready = $health.application -eq 'coi-dedicated-manager' } catch { }
    if ($ready) { break }
    Start-Sleep -Milliseconds 250
}
if (-not $ready) { throw "The manager is still starting. See $DataDir\manager.log" }
if (-not $NoBrowser) { Start-Process $url }
Write-Host "Server manager: $url"
Write-Host 'Closing the browser leaves your server running.'
