#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0-alpha.5',
    [string]$GameDir = $env:COI_GAME_DIR,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $GameDir) { $GameDir = 'F:\SteamLibrary\steamapps\common\Captain of Industry' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'Captain of Industry_Data\Managed\Mafi.Core.dll'))) { throw 'Supply -GameDir with an installed copy of Captain of Industry.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository "artifacts\releases\v$Version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$work = Join-Path $repository ('.work\release-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
$client = Join-Path $work "CoI-Multiplayer-Client-$Version-win-x64"
$server = Join-Path $work "CoI-Server-Manager-$Version-win-x64"
$clientPayload = Join-Path $client 'payload'
foreach ($directory in @($work, $client, $server, $clientPayload)) { [void][IO.Directory]::CreateDirectory($directory) }

function Build-Project([string[]]$BuildArgs) {
    & dotnet @BuildArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($BuildArgs[0])" }
}
function Download-Checked([string]$Url, [string]$Path, [string]$Sha256) {
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest $Url -OutFile $Path }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) { throw "Checksum mismatch: $([IO.Path]::GetFileName($Path))" }
}
function Make-Manifest([string]$Directory) {
    $prefix = $Directory.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $items = @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($prefix.Length).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    return ConvertTo-Json -InputObject $items -Depth 4
}

$dependencies = Join-Path $repository '.work\dependencies'
[void][IO.Directory]::CreateDirectory($dependencies)
$bepArchive = Join-Path $dependencies 'BepInEx_win_x64_5.4.23.5.zip'
Download-Checked 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip' $bepArchive '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'
$bep = Join-Path $work 'BepInEx'
Expand-Archive -LiteralPath $bepArchive -DestinationPath $bep
Build-Project -BuildArgs @('build', (Join-Path $repository 'src\CoiDedicated.Client\CoiDedicated.Client.csproj'), '-c', 'Release', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', (Join-Path $work 'client-build'), "-p:GameDir=$GameDir", "-p:BepInExDir=$(Join-Path $bep 'BepInEx\core')", "-p:BaseOutputPath=$(Join-Path $work 'build\client\')", "-p:Version=$Version")
Build-Project -BuildArgs @('publish', (Join-Path $repository 'src\CoiDedicated\CoiDedicated.csproj'), '-c', 'Release', '-p:DebugType=None', '-p:DebugSymbols=false', '-r', 'win-x64', '--self-contained', 'true', '-p:UseAppHost=true', '-p:PublishSingleFile=false', '-o', (Join-Path $server 'bin\server'), "-p:GameDir=$GameDir", "-p:BaseOutputPath=$(Join-Path $work 'build\host\')", "-p:Version=$Version")
Build-Project -BuildArgs @('publish', (Join-Path $repository 'src\CoiDedicated.Manager\CoiDedicated.Manager.csproj'), '-c', 'Release', '-p:DebugType=None', '-p:DebugSymbols=false', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-o', (Join-Path $server 'bin\manager'), "-p:BaseOutputPath=$(Join-Path $work 'build\manager\')", "-p:Version=$Version")

$pluginDestination = Join-Path $clientPayload 'BepInEx\plugins\CoiDedicated'
[void][IO.Directory]::CreateDirectory($pluginDestination)
foreach ($name in @('CoiDedicated.Client.dll','CoiDedicated.Protocol.dll','CoiDedicated.GameAdapter.dll')) { Copy-Item -LiteralPath (Join-Path $work "client-build\$name") -Destination $pluginDestination }
Copy-Item -LiteralPath (Join-Path $bep 'BepInEx\core') -Destination (Join-Path $clientPayload 'BepInEx') -Recurse
foreach ($name in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath (Join-Path $bep $name) -Destination $clientPayload }
Get-ChildItem -LiteralPath (Join-Path $repository 'distribution\client') -File | Copy-Item -Destination $client
Get-ChildItem -LiteralPath (Join-Path $repository 'distribution\server') -File | Copy-Item -Destination $server
foreach ($package in @($client,$server)) {
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE'), (Join-Path $repository 'THIRD-PARTY-NOTICES.md') -Destination $package
    Copy-Item -LiteralPath (Join-Path $repository 'distribution\third-party') -Destination $package -Recurse
    [ordered]@{ version=$Version; platform='win-x64'; gameVersion='0.8.7d'; compatibility='dedicated-prototype-5 / cmd1'; experimental=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'release.json') -Encoding utf8NoBOM
}
$doorstopSource = Join-Path $dependencies 'UnityDoorstop-v4.5.0-source.zip'
Download-Checked 'https://github.com/NeighTools/UnityDoorstop/archive/refs/tags/v4.5.0.zip' $doorstopSource '7F0C963104AA08BF5FEFEF8FF85E7FECD8306838F5AF3101487D9DB4E9188D63'
Copy-Item -LiteralPath $doorstopSource -Destination (Join-Path $client 'third-party')
Copy-Item -LiteralPath (Join-Path $repository 'docs\player-guide.md'), (Join-Path $repository 'docs\multiplayer-coverage.md') -Destination $client
Copy-Item -LiteralPath (Join-Path $repository 'docs\hosting-guide.md') -Destination $server
Make-Manifest $clientPayload | Set-Content -LiteralPath (Join-Path $client 'files.json') -Encoding utf8NoBOM

foreach ($package in @($client,$server)) {
    $forbidden = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.Name -match '^(Mafi|UnityEngine)(\.|$)|\.key$|\.save($|\.)|\.map$|^(profiles|process)\.json$' })
    if ($forbidden.Count) { throw "Release contains excluded game/private files: $($forbidden.Name -join ', ')" }
    $zip = Join-Path $OutputDirectory ((Split-Path $package -Leaf) + '.zip')
    if (Test-Path -LiteralPath $zip) { throw "Release already exists; choose another output directory: $zip" }
    Compress-Archive -LiteralPath $package -DestinationPath $zip -CompressionLevel Optimal
}
$hashes = Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.zip' -File | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$hashes | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $repository "docs\releases\v$Version.md") -Destination (Join-Path $OutputDirectory 'RELEASE-NOTES.md')
Write-Host "Release packages: $OutputDirectory"
Write-Host "Staged files: $work"
