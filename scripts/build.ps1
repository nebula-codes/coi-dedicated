param(
    [string]$GameDir = $(if ($env:COI_GAME_DIR) { $env:COI_GAME_DIR } else { 'F:\SteamLibrary\steamapps\common\Captain of Industry' }),
    [switch]$ServerOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'Captain of Industry_Data\Managed\Mafi.Core.dll'))) { throw 'GameDir does not contain the installed game assemblies.' }
$projects = @('CoiDedicated', 'CoiDedicated.Control', 'CoiDedicated.Peer', 'CoiDedicated.Manager')
$bepCore = Join-Path $projectRoot '.work\dependencies\BepInEx_win_x64_5.4.23.5\BepInEx\core'
if (-not $ServerOnly) {
    if (-not (Test-Path -LiteralPath (Join-Path $bepCore 'BepInEx.dll'))) {
        $dependencyDir = Join-Path $projectRoot '.work\dependencies'
        New-Item -ItemType Directory -Path $dependencyDir -Force | Out-Null
        $archive = Join-Path $dependencyDir 'BepInEx_win_x64_5.4.23.5.zip'
        if (-not (Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip' -OutFile $archive
        }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4') { throw 'BepInEx archive checksum mismatch.' }
        Expand-Archive -LiteralPath $archive -DestinationPath (Split-Path (Split-Path $bepCore -Parent) -Parent) -Force
    }
    $projects += 'CoiDedicated.Client'
}
foreach ($project in $projects) {
    & dotnet build (Join-Path $projectRoot "src\$project\$project.csproj") -c Release "-p:GameDir=$GameDir" "-p:BepInExDir=$bepCore"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
& dotnet build (Join-Path $projectRoot 'src\CoiDedicated\CoiDedicated.csproj') -c Release "-p:GameDir=$GameDir" -o (Join-Path $projectRoot 'artifacts\manager-host')
if ($LASTEXITCODE -ne 0) { throw 'Manager host build failed.' }
Write-Host 'Build complete. Open Server Manager.cmd for the dashboard, or scripts/start-server.ps1 for the CLI.'
