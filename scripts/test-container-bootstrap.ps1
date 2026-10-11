#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Image = 'coi-dedicated:container-test',
    [string]$GameDir = $env:COI_GAME_DIR,
    [Parameter(Mandatory = $true)][string]$SaveFile
)
$ErrorActionPreference = 'Stop'
if (-not $GameDir) { $GameDir = 'F:\SteamLibrary\steamapps\common\Captain of Industry' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$SaveFile = (Resolve-Path -LiteralPath $SaveFile).Path
if (-not (Test-Path -LiteralPath $SaveFile -PathType Leaf)) { throw 'SaveFile must be a saved island fixture.' }
$repository = Split-Path $PSScriptRoot -Parent
$id = [Guid]::NewGuid().ToString('N')
$name = "coi-bootstrap-test-$id"
$volume = "$name-data"
$work = Join-Path $repository ".work/bootstrap-test-$id"
[void][IO.Directory]::CreateDirectory($work)
$created = $false

function Docker-Checked([string[]]$Arguments) {
    $result = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Docker $($Arguments[0]) failed: $result" }
    return $result
}

try {
    $initialHash = (Get-FileHash -LiteralPath $SaveFile -Algorithm SHA256).Hash
    [void](Docker-Checked @('volume', 'create', $volume))
    # The image's app user owns a fresh /data volume. Only this copied fixture is
    # writable; the user's game installation and input save remain read-only.
    [void](Docker-Checked @('run', '--rm', '--network', 'none', '--read-only', '--cap-drop', 'ALL',
        '--security-opt', 'no-new-privileges:true', '--entrypoint', '/bin/sh',
        '--mount', "type=bind,source=$SaveFile,target=/fixture.save,readonly",
        '--mount', "type=volume,source=$volume,target=/data",
        $Image, '-c', 'cp /fixture.save /data/island.save'))
    [void](Docker-Checked @('run', '-d', '--name', $name, '--init', '--network', 'none', '--read-only',
        '--tmpfs', '/tmp:rw,noexec,nosuid,size=128m', '--cap-drop', 'ALL',
        '--security-opt', 'no-new-privileges:true',
        '--mount', "type=bind,source=$GameDir,target=/game,readonly",
        '--mount', "type=volume,source=$volume,target=/data", $Image))
    $created = $true

    $observedChild = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $state = (Docker-Checked @('inspect', $name) | ConvertFrom-Json)[0].State
        if (-not $state.Running) { throw "Container exited before bootstrap interruption: $($state.ExitCode)" }
        $processes = Docker-Checked @('top', $name, '-eo', 'pid,args')
        if (($processes -join "`n") -match '--data-dir /data/\.bootstrap/canonical') {
            $processes | Set-Content -LiteralPath (Join-Path $work 'bootstrap-processes.txt')
            $observedChild = $true
            break
        }
        $logs = Docker-Checked @('logs', $name)
        if (($logs -join "`n") -match '\[server\] Authenticated control endpoint') {
            throw 'Bootstrap completed before the child was observed; this run did not test interruption.'
        }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $observedChild) { throw 'Did not observe a live canonicalization child within 45 seconds.' }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    [void](Docker-Checked @('stop', '--timeout', '300', $name))
    $stopwatch.Stop()
    $state = (Docker-Checked @('inspect', $name) | ConvertFrom-Json)[0].State
    if ($state.Running -or $state.Pid -ne 0 -or $state.ExitCode -ne 0 -or $state.OOMKilled) {
        throw "Bootstrap did not stop cleanly: $($state | ConvertTo-Json -Compress)"
    }
    $logs = Docker-Checked @('logs', $name)
    $logs | Set-Content -LiteralPath (Join-Path $work 'container.log')
    # A managed host redirects its console to the manager's persistent log.
    # Read that output too so a completed bootstrap cannot look interrupted.
    $runtimeLogs = Join-Path $work 'runtime'
    & docker cp "${name}:/data/.manager/runtime" $runtimeLogs 2>$null
    if ($LASTEXITCODE -eq 0) {
        $logs = @($logs) + @(Get-ChildItem -LiteralPath $runtimeLogs -Filter server.log -File -Recurse | ForEach-Object { Get-Content -LiteralPath $_.FullName })
    }
    if (($logs -join "`n") -match '\[bootstrap\] Canonical save ready') {
        throw 'Canonicalization completed before SIGTERM; this run did not test interruption.'
    }

    $preserved = Join-Path $work 'preserved-island.save'
    [void](Docker-Checked @('cp', "${name}:/data/island.save", $preserved))
    [void](Docker-Checked @('cp', "${name}:/data/.bootstrap", (Join-Path $work 'bootstrap')))
    $preservedHash = (Get-FileHash -LiteralPath $preserved -Algorithm SHA256).Hash
    if ($preservedHash -ne $initialHash) { throw 'Stopping bootstrap changed the existing island save.' }
    if ((Get-FileHash -LiteralPath $SaveFile -Algorithm SHA256).Hash -ne $initialHash) {
        throw 'The source fixture unexpectedly changed.'
    }

    [ordered]@{
        image = $Image
        result = 'passed'
        bootstrapChildObserved = $true
        exitCode = $state.ExitCode
        remainingContainerPid = $state.Pid
        stopSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        initialSha256 = $initialHash
        preservedSha256 = $preservedHash
        volume = $volume
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'results.json')
    Write-Host 'PASS SIGTERM interrupted the active bootstrap child, exited 0, and preserved the existing island byte-for-byte.'
}
finally {
    if ($created) {
        & docker logs $name *> (Join-Path $work 'container.log')
        & docker stop --timeout 300 $name *> $null
        & docker rm $name *> $null
    }
    # Keep this isolated volume and evidence for inspection. Never remove user data.
    Write-Host "Test evidence: $work; test volume: $volume"
}
