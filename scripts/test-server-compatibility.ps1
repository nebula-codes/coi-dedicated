#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HostDirectory,
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$SaveFile
)
$ErrorActionPreference = 'Stop'
$HostDirectory = (Resolve-Path -LiteralPath $HostDirectory).Path
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$SaveFile = (Resolve-Path -LiteralPath $SaveFile).Path
$repository = Split-Path $PSScriptRoot -Parent
$work = Join-Path $repository ('.work/compatibility-test-' + [Guid]::NewGuid().ToString('N'))
$managed = Join-Path $GameDir 'Captain of Industry_Data/Managed'
$names = @('Mafi.dll', 'Mafi.Core.dll', 'Mafi.Base.dll')
$originalHashes = @{}
foreach ($name in $names) { $originalHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $managed $name)).Hash }
$saveHash = (Get-FileHash -LiteralPath $SaveFile).Hash
$results = @()
foreach ($caseName in @('Mafi.dll', 'Mafi.Core.dll', 'Mafi.Base.dll', 'missing-base')) {
    $caseDir = Join-Path $work $caseName
    $game = Join-Path $caseDir 'game'
    $copiedManaged = Join-Path $game 'Captain of Industry_Data/Managed'
    $data = Join-Path $caseDir 'data'
    [void][IO.Directory]::CreateDirectory($copiedManaged)
    [void][IO.Directory]::CreateDirectory($data)
    foreach ($name in $names) {
        if ($caseName -eq 'missing-base' -and $name -eq 'Mafi.Base.dll') { continue }
        Copy-Item -LiteralPath (Join-Path $managed $name) -Destination $copiedManaged
    }
    if ($caseName -ne 'missing-base') {
        # Change only the isolated copy. The real game installation is read-only.
        $copy = Join-Path $copiedManaged $caseName
        $stream = [IO.File]::Open($copy, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
        try {
            $oldByte = $stream.ReadByte()
            $stream.Position = 0
            $stream.WriteByte([byte]($oldByte -bxor 1))
        } finally { $stream.Dispose() }
    }
    $island = Join-Path $data 'island.save'
    Copy-Item -LiteralPath $SaveFile -Destination $island
    $log = Join-Path $caseDir 'host.log'
    & dotnet (Join-Path $HostDirectory 'CoiDedicated.dll') --game-dir $game --data-dir $data --serve *> $log
    $code = $LASTEXITCODE
    $message = Get-Content -LiteralPath $log -Raw
    if ($code -ne 1) { throw "$caseName was not refused with exit 1: $code" }
    $expected = if ($caseName -eq 'missing-base') { 'Missing Mafi.Base.dll' } else { 'Unsupported CoI game build' }
    if (-not $message.Contains($expected)) { throw "$caseName failed for an unrelated reason; see $log" }
    if ((Get-FileHash -LiteralPath $island).Hash -ne $saveHash) { throw "$caseName changed the island" }
    if (Test-Path -LiteralPath (Join-Path $data '.bootstrap')) { throw "$caseName entered bootstrap" }
    if (Test-Path -LiteralPath (Join-Path $data '.server.lock')) { throw "$caseName acquired the server lease before validation" }
    $results += [ordered]@{ case = $caseName; exitCode = $code; islandUnchanged = $true; bootstrapStarted = $false }
}
foreach ($name in $names) {
    if ((Get-FileHash -LiteralPath (Join-Path $managed $name)).Hash -ne $originalHashes[$name]) { throw "Source game file changed: $name" }
}
if ((Get-FileHash -LiteralPath $SaveFile).Hash -ne $saveHash) { throw 'Source save changed' }
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'results.json') -Encoding utf8NoBOM
Write-Host "PASS four unsupported/missing assembly cases refused before bootstrap; all saves and source game files unchanged. Evidence: $work"
