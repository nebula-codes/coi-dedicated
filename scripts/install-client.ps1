param([string]$GameDir = $(if ($env:COI_GAME_DIR) { $env:COI_GAME_DIR } else { 'F:\SteamLibrary\steamapps\common\Captain of Industry' }))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (Get-Process -Name 'Captain of Industry' -ErrorAction SilentlyContinue) { throw 'Close Captain of Industry before installing or updating the plugin.' }
$dependencyRoot = Join-Path $projectRoot '.work\dependencies\BepInEx_win_x64_5.4.23.5'
$pluginRoot = Join-Path $projectRoot 'src\CoiDedicated.Client\bin\Release\netstandard2.1'
$payloads = [System.Collections.Generic.List[object]]::new()
if (-not (Test-Path -LiteralPath (Join-Path $dependencyRoot 'BepInEx\core\BepInEx.dll'))) { throw 'Run scripts/build.ps1 first.' }
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $dependencyRoot 'BepInEx\core') -File) {
    $payloads.Add(@{ Source = $source.FullName; Relative = "BepInEx\core\$($source.Name)"; Plugin = $false })
}
foreach ($name in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
    $payloads.Add(@{ Source = (Join-Path $dependencyRoot $name); Relative = $name; Plugin = $false })
}
foreach ($name in @('CoiDedicated.Client.dll', 'CoiDedicated.Protocol.dll', 'CoiDedicated.GameAdapter.dll')) {
    $payloads.Add(@{ Source = (Join-Path $pluginRoot $name); Relative = "BepInEx\plugins\CoiDedicated\$name"; Plugin = $true })
}
# Check all conflicts before writing any files. Existing third-party loaders are never replaced.
foreach ($item in $payloads) {
    if (-not (Test-Path -LiteralPath $item.Source)) { throw "Missing build/dependency file: $($item.Source)" }
    $destination = Join-Path $GameDir $item.Relative
    if ((Test-Path -LiteralPath $destination) -and -not $item.Plugin -and
        (Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $item.Source).Hash) {
        throw "Existing loader file differs; installation stopped without changes: $destination"
    }
}
$recordDir = Join-Path $projectRoot ('artifacts\client-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $recordDir -Force | Out-Null
$records = foreach ($item in $payloads) {
    $destination = Join-Path $GameDir $item.Relative
    $existed = Test-Path -LiteralPath $destination
    $backup = $null
    if ($existed -and (Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $item.Source).Hash) {
        $backup = Join-Path $recordDir $item.Relative
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $destination -Destination $backup
    }
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $item.Source -Destination $destination -Force
    [pscustomobject]@{ Path = $destination; SHA256 = (Get-FileHash -LiteralPath $destination).Hash; ExistedBefore = $existed; Backup = $backup }
}
$records | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $recordDir 'manifest.json')
Write-Host "Installed dedicated client. Press F8 at the game's main menu. Manifest: $recordDir"
