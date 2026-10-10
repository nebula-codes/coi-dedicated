#requires -Version 5.1
param(
    [string]$GameDir,
    [ValidateRange(1024, 65535)][int]$Port = 28560,
    [string]$DataDir,
    [switch]$NoBrowser,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$managerExe = Join-Path $packageRoot 'bin\manager\CoiDedicated.Manager.exe'
$hostDll = Join-Path $packageRoot 'bin\server\CoiDedicated.dll'
$hostExe = Join-Path $packageRoot 'bin\server\CoiDedicated.exe'

function Test-GameDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    foreach ($relative in @('Captain of Industry_Data\Managed\Mafi.dll', 'Captain of Industry_Data\Managed\Mafi.Core.dll', 'Captain of Industry_Data\Managed\Mafi.Base.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $relative) -PathType Leaf)) { return $false }
    }
    return (Test-Path -LiteralPath (Join-Path $Path 'Maps') -PathType Container)
}

function Find-GameDirectory {
    if (Test-GameDirectory $env:COI_GAME_DIR) { return [IO.Path]::GetFullPath($env:COI_GAME_DIR) }
    $steamRoots = @()
    foreach ($registry in @(
        @{ Path = 'HKCU:\Software\Valve\Steam'; Value = 'SteamPath' },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Value = 'InstallPath' },
        @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Value = 'InstallPath' }
    )) {
        $entry = Get-ItemProperty -LiteralPath $registry.Path -ErrorAction SilentlyContinue
        if ($entry -and $entry.($registry.Value)) { $steamRoots += [string]$entry.($registry.Value) }
    }
    if (${env:ProgramFiles(x86)}) { $steamRoots += Join-Path ${env:ProgramFiles(x86)} 'Steam' }
    if ($env:ProgramFiles) { $steamRoots += Join-Path $env:ProgramFiles 'Steam' }
    $libraries = @($steamRoots)
    foreach ($steamRoot in ($steamRoots | Select-Object -Unique)) {
        $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryFile) {
            try { $contents = Get-Content -LiteralPath $libraryFile -Raw } catch { continue }
            foreach ($match in [regex]::Matches($contents, '"path"\s+"((?:\\.|[^"\\])*)"')) {
                $libraries += $match.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        $manifest = Join-Path $library 'steamapps\appmanifest_1594320.acf'
        $installName = 'Captain of Industry'
        if (Test-Path -LiteralPath $manifest) {
            try {
                $match = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"installdir"\s+"([^"]+)"')
                if ($match.Success) { $installName = $match.Groups[1].Value }
            } catch { }
        }
        $candidate = Join-Path (Join-Path $library 'steamapps\common') $installName
        if (Test-GameDirectory $candidate) { return [IO.Path]::GetFullPath($candidate) }
    }
    return $null
}

foreach ($required in @($managerExe, $hostExe, $hostDll)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "The server package is incomplete. Extract the entire server ZIP before launching. Missing: $required"
    }
}
if (-not [Environment]::Is64BitOperatingSystem) { throw 'This server package requires 64-bit Windows.' }
if ($GameDir) {
    $GameDir = [IO.Path]::GetFullPath($GameDir)
    if (-not (Test-GameDirectory $GameDir)) { throw 'GameDir must point to your installed Captain of Industry folder, containing its Maps and Captain of Industry_Data folders.' }
}
else { $GameDir = Find-GameDirectory }

if ($GameDir) {
    # Process-local only: existing profile settings are preserved, and no system environment is modified.
    $env:COI_GAME_DIR = $GameDir
    Write-Host "Captain of Industry: $GameDir"
}
else { Write-Host 'Captain of Industry was not found. Set its installation folder in the dashboard Settings before starting a server.' }
if (-not $DataDir) { $DataDir = Join-Path $packageRoot 'data\default' }
$DataDir = [IO.Path]::GetFullPath($DataDir)
if ($Check) {
    Write-Host 'Server and manager executables are present. The package includes its own .NET runtime.'
    Write-Host "Manager data: $DataDir"
    return
}

$url = "http://127.0.0.1:$Port"
$existing = $null
try { $existing = Invoke-RestMethod "$url/api/health" -TimeoutSec 2 } catch { }
if ($existing -and $existing.application -eq 'coi-dedicated-manager') {
    if (-not $NoBrowser) { Start-Process $url }
    Write-Host "Server manager is already available at $url"
    Write-Host 'Existing profiles keep their saved game installation settings.'
    return
}

New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
# Windows Start-Process joins ArgumentList strings. Quote paths, including trailing backslashes.
function Quote-Argument([string]$Value) {
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}
$arguments = @('--repository', (Quote-Argument $packageRoot), '--data-dir', (Quote-Argument $DataDir), '--host-dll', (Quote-Argument $hostDll), '--port', "$Port")
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$stdout = Join-Path $DataDir "manager-$stamp.log"
$stderr = Join-Path $DataDir "manager-$stamp-errors.log"
$launched = Start-Process -FilePath $managerExe -ArgumentList $arguments -WorkingDirectory $packageRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    if ($launched.HasExited) { throw "The manager could not start. Check $stderr. Port $Port may already be used by another application." }
    try { $health = Invoke-RestMethod "$url/api/health" -TimeoutSec 1; $ready = $health.application -eq 'coi-dedicated-manager' } catch { }
    if ($ready) { break }
    Start-Sleep -Milliseconds 250
}
if (-not $ready) { throw "The manager is still starting. Check $stdout or open $url later." }
if (-not $NoBrowser) { Start-Process $url }
Write-Host "Server manager: $url"
Write-Host 'Use the dashboard to start your island. Closing the browser leaves the manager and server running.'
