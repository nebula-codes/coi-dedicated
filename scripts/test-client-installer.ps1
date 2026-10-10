#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$root = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $root ('.work\client-installer-tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$source = Join-Path $root 'distribution\client\install-client.ps1'
$script:passed = 0
$paths = @('BepInEx/plugins/CoiDedicated/CoiDedicated.Client.dll', 'BepInEx/plugins/CoiDedicated/CoiDedicated.Protocol.dll', 'BepInEx/plugins/CoiDedicated/CoiDedicated.GameAdapter.dll', 'BepInEx/core/BepInEx.dll', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version')

function Write-FixtureFile([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding $false))
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function New-Fixture([string]$Name) {
    $folder = Join-Path $testRoot $Name
    $package = Join-Path $folder 'package'
    $game = Join-Path $folder 'fake-game'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $package 'install-client.ps1')
    foreach ($relative in @('Captain of Industry.exe', 'UnityPlayer.dll', 'Captain of Industry_Data/Managed/Mafi.dll', 'Captain of Industry_Data/Managed/Mafi.Core.dll', 'Captain of Industry_Data/Managed/Mafi.Base.dll', 'Captain of Industry_Data/Managed/Mafi.Unity.dll', 'Captain of Industry_Data/Managed/UnityEngine.CoreModule.dll')) { Write-FixtureFile (Join-Path $game $relative) ('fixture game file: ' + $relative) }
    foreach ($relative in $paths) { Write-FixtureFile (Join-Path (Join-Path $package 'payload') $relative) ('payload v1: ' + $relative) }
    $fixture = [pscustomobject]@{ Folder = $folder; Package = $package; Game = $game }
    Write-Manifest $fixture
    return $fixture
}
function Write-Manifest($Fixture) {
    $entries = @($paths | ForEach-Object { [pscustomobject]@{ path = $_; sha256 = Hash (Join-Path (Join-Path $Fixture.Package 'payload') $_) } })
    $entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Fixture.Package 'files.json') -Encoding UTF8
}
function Run-Installer($Fixture, [bool]$Success, [string]$Expected = '', [switch]$Uninstall) {
    $arguments = @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Fixture.Package 'install-client.ps1'), '-GameDir', $Fixture.Game, '-NonInteractive')
    if ($Uninstall) { $arguments += '-Uninstall' }
    $output = & powershell.exe @arguments 2>&1
    $exitCode = $LASTEXITCODE
    if (($exitCode -eq 0) -ne $Success) { throw "Unexpected installer exit $exitCode for $($Fixture.Folder): $($output -join [Environment]::NewLine)" }
    if ($Expected.Length -gt 0 -and ($output -join "`n") -notmatch [regex]::Escape($Expected)) { throw "Expected '$Expected' in installer output: $($output -join [Environment]::NewLine)" }
}
function Pass([string]$Name) { $script:passed++; Write-Host "PASS $Name" }

# Every destination in these tests is a newly created fixture below .work.
# No real Steam game, registry entry, or installed mod is changed.
$fixture = New-Fixture 'fresh-install'
Run-Installer $fixture $true
foreach ($relative in $paths) { Assert ((Hash (Join-Path $fixture.Game $relative)) -eq (Hash (Join-Path (Join-Path $fixture.Package 'payload') $relative))) "Installed file differs: $relative" }
Assert (Test-Path -LiteralPath (Join-Path $fixture.Game 'BepInEx/CoiDedicatedInstaller/installed.json')) 'Installation record missing.'
Pass 'fresh installation under Windows PowerShell 5.1'

$client = 'BepInEx/plugins/CoiDedicated/CoiDedicated.Client.dll'
$oldHash = Hash (Join-Path $fixture.Game $client)
Write-FixtureFile (Join-Path (Join-Path $fixture.Package 'payload') $client) 'payload v2'
Write-Manifest $fixture
Run-Installer $fixture $true
Assert ((Hash (Join-Path $fixture.Game $client)) -ne $oldHash) 'Client did not update.'
$backups = @(Get-ChildItem -LiteralPath (Join-Path $fixture.Game 'BepInEx/CoiDedicatedInstaller/backups') -Recurse -File | Where-Object { $_.Name -eq 'CoiDedicated.Client.dll' })
Assert ($backups.Count -eq 1 -and (Hash $backups[0].FullName) -eq $oldHash) 'Previous client DLL was not backed up.'
Run-Installer $fixture $true
Pass 'updates preserve prior DLLs; repeat installation is idempotent'

$conflict = New-Fixture 'loader-conflict'
Write-FixtureFile (Join-Path $conflict.Game 'doorstop_config.ini') 'third party loader config'
Run-Installer $conflict $false 'Existing loader file differs'
Assert (-not (Test-Path -LiteralPath (Join-Path $conflict.Game 'BepInEx'))) 'Loader conflict wrote plugin files before failing.'
Assert ((Get-Content -LiteralPath (Join-Path $conflict.Game 'doorstop_config.ini') -Raw) -eq 'third party loader config') 'Existing loader was overwritten.'
Pass 'all loader conflicts are preflighted without installation writes'

$blockedRecord = New-Fixture 'blocked-record-directory'
Write-FixtureFile (Join-Path $blockedRecord.Game 'BepInEx/CoiDedicatedInstaller') 'unrelated file blocks metadata'
Run-Installer $blockedRecord $false 'blocks an installation folder'
Assert (-not (Test-Path -LiteralPath (Join-Path $blockedRecord.Game 'winhttp.dll'))) 'Metadata path conflict was not preflighted.'
Assert (-not (Test-Path -LiteralPath (Join-Path $blockedRecord.Game $client))) 'Metadata path conflict wrote a plugin.'
Pass 'installation metadata path conflicts are preflighted'

$corrupt = New-Fixture 'checksum-mismatch'
Write-FixtureFile (Join-Path (Join-Path $corrupt.Package 'payload') 'winhttp.dll') 'damaged download'
Run-Installer $corrupt $false 'checksum mismatch'
Assert (-not (Test-Path -LiteralPath (Join-Path $corrupt.Game 'BepInEx'))) 'Checksum failure modified destination.'
Pass 'corrupted payload is rejected before writes'

$missingHash = New-Fixture 'missing-hash'
$manifestPath = Join-Path $missingHash.Package 'files.json'
$entries = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $manifestPath -Raw)
$entries[0].PSObject.Properties.Remove('sha256')
$entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Run-Installer $missingHash $false 'path and sha256'
Assert (-not (Test-Path -LiteralPath (Join-Path $missingHash.Game 'BepInEx'))) 'Missing hash modified destination.'
Pass 'missing manifest hashes are rejected'

$unsafe = New-Fixture 'unsafe-path'
$manifestPath = Join-Path $unsafe.Package 'files.json'
$entries = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $manifestPath -Raw)
$entries += [pscustomobject]@{ path = '../escape.dll'; sha256 = ('a' * 64) }
$entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Run-Installer $unsafe $false 'Unsafe package path'
Assert (-not (Test-Path -LiteralPath (Join-Path $unsafe.Folder 'escape.dll'))) 'Traversal escaped fixture game root.'
Assert (-not (Test-Path -LiteralPath (Join-Path $unsafe.Game 'BepInEx'))) 'Unsafe path modified destination.'
Pass 'manifest traversal paths are rejected'

$extra = New-Fixture 'unlisted-payload'
Write-FixtureFile (Join-Path $extra.Package 'payload/BepInEx/core/unknown.dll') 'unlisted payload'
Run-Installer $extra $false 'Unlisted payload file'
Assert (-not (Test-Path -LiteralPath (Join-Path $extra.Game 'BepInEx'))) 'Unlisted payload modified destination.'
Pass 'every payload file must be covered by the manifest'

$duplicate = New-Fixture 'duplicate-path'
$manifestPath = Join-Path $duplicate.Package 'files.json'
$entries = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $manifestPath -Raw)
$entries += [pscustomobject]@{ path = $entries[0].path.ToUpperInvariant(); sha256 = $entries[0].sha256 }
$entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Run-Installer $duplicate $false 'Duplicate package path'
Pass 'Windows case-insensitive duplicate paths are rejected'

$junction = New-Fixture 'junction-destination'
$outside = Join-Path $junction.Folder 'unrelated-folder'
New-Item -ItemType Directory -Path $outside -Force | Out-Null
New-Item -ItemType Junction -Path (Join-Path $junction.Game 'BepInEx') -Target $outside | Out-Null
Run-Installer $junction $false 'Symbolic links and junctions'
Assert (@(Get-ChildItem -LiteralPath $outside -Force).Count -eq 0) 'Junction caused writes outside the selected game directory.'
Pass 'destination junctions cannot redirect installation'

$rollback = New-Fixture 'rollback'
Run-Installer $rollback $true
$before = @{}
foreach ($relative in $paths[0..2]) { $before[$relative] = Hash (Join-Path $rollback.Game $relative); Write-FixtureFile (Join-Path (Join-Path $rollback.Package 'payload') $relative) ('update: ' + $relative) }
Write-Manifest $rollback
$locked = [IO.File]::Open((Join-Path $rollback.Game $paths[1]), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Run-Installer $rollback $false } finally { $locked.Dispose() }
foreach ($relative in $paths[0..2]) { Assert ((Hash (Join-Path $rollback.Game $relative)) -eq $before[$relative]) "Rollback did not preserve $relative" }
Pass 'failed update rolls back previously written plugin files'

Write-FixtureFile (Join-Path $fixture.Game 'BepInEx/plugins/OtherMod.dll') 'unrelated mod'
$loaderHash = Hash (Join-Path $fixture.Game 'winhttp.dll')
Run-Installer $fixture $true '' -Uninstall
foreach ($relative in $paths[0..2]) { Assert (-not (Test-Path -LiteralPath (Join-Path $fixture.Game $relative))) "Uninstall left dedicated DLL: $relative" }
Assert ((Hash (Join-Path $fixture.Game 'winhttp.dll')) -eq $loaderHash) 'Uninstall changed shared loader.'
Assert (Test-Path -LiteralPath (Join-Path $fixture.Game 'BepInEx/plugins/OtherMod.dll')) 'Uninstall removed another mod.'
Pass 'uninstall removes only owned plugin DLLs and preserves loader/other mods'

$changed = New-Fixture 'changed-plugin-uninstall'
Run-Installer $changed $true
Write-FixtureFile (Join-Path $changed.Game $client) 'user modified plugin'
Run-Installer $changed $false 'changed since installation' -Uninstall
foreach ($relative in $paths[0..2]) { Assert (Test-Path -LiteralPath (Join-Path $changed.Game $relative)) 'Uninstall partially removed files before a checksum conflict.' }
Pass 'uninstall preflights modified plugin conflicts'

Write-Host "$script:passed installer checks passed. Fixtures retained at $testRoot"
