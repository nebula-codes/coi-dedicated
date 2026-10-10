param(
    [string]$GameDir = $(if ($env:COI_GAME_DIR) { $env:COI_GAME_DIR } else { 'F:\SteamLibrary\steamapps\common\Captain of Industry' }),
    [string]$DataDir,
    [string]$Listen = '127.0.0.1',
    [int]$GamePort = 28551,
    [int]$ControlPort = 28550,
    [string]$Map = 'New Haven',
    [string]$Load,
    [int]$AutosaveSeconds = 60
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $DataDir) { $DataDir = Join-Path $projectRoot 'server-data' }
$hostDll = Join-Path $projectRoot 'src\CoiDedicated\bin\Release\net10.0\CoiDedicated.dll'
if (-not (Test-Path -LiteralPath $hostDll)) { throw 'Run scripts/build.ps1 first.' }
$hostArguments = @($hostDll, '--serve', '--game-dir', $GameDir, '--data-dir', $DataDir, '--listen', $Listen,
    '--game-port', "$GamePort", '--port', "$ControlPort", '--map', $Map, '--starting-factory', '--autosave-seconds', "$AutosaveSeconds")
if ($Load) { $hostArguments += @('--load', $Load) }
& dotnet @hostArguments
exit $LASTEXITCODE
