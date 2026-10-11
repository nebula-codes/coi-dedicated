#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Image = 'coi-dedicated:container-test',
    [string]$GameDir = $env:COI_GAME_DIR
)
$ErrorActionPreference = 'Stop'
throw 'This archived test targets the unauthenticated native Linux image and cannot validate the Wine/Steam stack. See docs/docker-portainer.md for current validation requirements.'
if (-not $GameDir) { $GameDir = 'F:\SteamLibrary\steamapps\common\Captain of Industry' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$repository = Split-Path $PSScriptRoot -Parent
$id = [Guid]::NewGuid().ToString('N')
$name = "coi-container-test-$id"
$volume = "$name-data"
$work = Join-Path $repository ".work/container-test-$id"
[void][IO.Directory]::CreateDirectory($work)

function Docker-Checked([string[]]$Arguments) {
    $result = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Docker $($Arguments[0]) failed: $result" }
    return $result
}
function Wait-Ready {
    $deadline = [DateTime]::UtcNow.AddMinutes(4)
    do {
        $state = (Docker-Checked @('inspect', $name) | ConvertFrom-Json)[0].State
        if (-not $state.Running) { throw "Container exited before readiness: $($state.ExitCode)" }
        $status = & docker exec $name /app/control.sh --command status 2>$null
        if ($LASTEXITCODE -eq 0) { return ($status | ConvertFrom-Json) }
        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Container did not become ready within four minutes.'
}
function Stop-And-Check {
    $before = (Docker-Checked @('exec', $name, '/app/control.sh', '--command', 'status')) | ConvertFrom-Json
    [void](Docker-Checked @('stop', '--timeout', '120', $name))
    $state = (Docker-Checked @('inspect', $name) | ConvertFrom-Json)[0].State
    if ($state.ExitCode -ne 0 -or $state.OOMKilled) { throw "Container did not save and stop cleanly: $($state | ConvertTo-Json -Compress)" }
    [void](Docker-Checked @('cp', "${name}:/data/world-status.json", (Join-Path $work 'stopped-world.json')))
    $saved = Get-Content -LiteralPath (Join-Path $work 'stopped-world.json') -Raw | ConvertFrom-Json
    if ($saved.SimStep -lt $before.step) { throw 'Final save lost acknowledged simulation ticks.' }
    return $saved
}

try {
    [void](Docker-Checked @('volume','create',$volume))
    [void](Docker-Checked @('run','-d','--name',$name,'--init','--read-only',
        '--tmpfs','/tmp:rw,noexec,nosuid,size=128m','--cap-drop','ALL','--security-opt','no-new-privileges:true',
        '--mount',"type=bind,source=$GameDir,target=/game,readonly",
        '--mount',"type=volume,source=$volume,target=/data",
        '-p','127.0.0.1::28551','-e','COI_AUTOSAVE_SECONDS=600', $Image))
    $initial = Wait-Ready
    if ($initial.world.EntityCount -le 0) { throw 'Expected a populated island.' }
    Write-Host "PASS Linux populated island ready at step $($initial.step)"

    # Use the real game simulation in a Windows process to exercise snapshot,
    # command authentication, and authoritative tick replay across the OS boundary.
    [void](Docker-Checked @('cp', "${name}:/data/join.key", (Join-Path $work 'join.key')))
    $port = (Docker-Checked @('inspect', $name) | ConvertFrom-Json)[0].NetworkSettings.Ports.'28551/tcp'[0].HostPort
    $peerArguments = @('run', '--project', (Join-Path $repository 'src/CoiDedicated.Peer'), '-c', 'Release', "-p:GameDir=$GameDir", '--',
        '--game-dir', $GameDir, '--host', '127.0.0.1', '--port', $port, '--secret-file', (Join-Path $work 'join.key'),
        '--data-dir', (Join-Path $work 'peer'), '--ticks', '400', '--pause-at', '10', '--resume-at', '20',
        '--speed-at', '40:2', '--speed-at', '100:3', '--speed-at', '180:4', '--speed-at', '300:1',
        '--reject-speed-at', '30', '--place-machine-at', '220', '--name', 'docker-windows-peer')
    & dotnet @peerArguments *> (Join-Path $work 'peer.log')
    if ($LASTEXITCODE -ne 0) { throw "Windows peer failed; see $work/peer.log" }
    Write-Host 'PASS Windows gameplay peer replayed 400 ticks, shared speeds 1-4, checkpoints, and native building batch at 4x'

    # These two peers exercise presence framing/expiry only, not simulation parity.
    & node (Join-Path $repository 'scripts/test-presence.mjs') $port (Join-Path $work 'join.key')
    if ($LASTEXITCODE -ne 0) { throw 'Container presence relay fixture failed.' }

    [void](Docker-Checked @('exec', $name, '/app/control.sh', '--command', 'snapshot', '--output', '/data/test-snapshot.save'))
    [void](Docker-Checked @('cp', "${name}:/data/test-snapshot.save", (Join-Path $work 'snapshot.save')))
    if ((Get-Item -LiteralPath (Join-Path $work 'snapshot.save')).Length -lt 1000) { throw 'Snapshot is unexpectedly small.' }
    $stopped = Stop-And-Check
    Write-Host "PASS Docker SIGTERM saved step $($stopped.SimStep) and exited 0"
    $keyHash = (Get-FileHash -LiteralPath (Join-Path $work 'join.key')).Hash

    [void](Docker-Checked @('start', $name))
    $resumed = Wait-Ready
    if ($resumed.step -lt $stopped.SimStep) { throw 'Container restart did not resume its previous save.' }
    [void](Docker-Checked @('cp', "${name}:/data/join.key", (Join-Path $work 'restarted-join.key')))
    if ((Get-FileHash -LiteralPath (Join-Path $work 'restarted-join.key')).Hash -ne $keyHash) { throw 'Player key changed after restart.' }
    [void](Stop-And-Check)
    Write-Host 'PASS Persistent volume restored island and preserved player key on restart'
    [ordered]@{image=$Image; initialStep=$initial.step; stoppedStep=$stopped.SimStep; resumedStep=$resumed.step; windowsPeerTicks=400; presenceChecks=4; result='passed'} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'results.json')
}
finally {
    & docker logs $name *> (Join-Path $work 'container.log')
    & docker stop --timeout 120 $name *> $null
    & docker rm $name *> $null
    # Preserve test saves for inspection; remove only this named volume manually when no longer needed.
    Write-Host "Test evidence: $work; test volume: $volume"
}
