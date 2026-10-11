#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Image,
    [Parameter(Mandatory)][string]$SaveFile,
    [string]$GameDir = 'F:\SteamLibrary\steamapps\common\Captain of Industry',
    [int]$ManagerPort = 28664,
    [int]$GamePort = 28665,
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$SaveFile = (Resolve-Path -LiteralPath $SaveFile).Path
$id = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$name = "coi-manager-test-$id"
$volume = "$name-data"
$work = Join-Path $repository ".work/manager-container-$id"
New-Item -ItemType Directory -Path $work | Out-Null
$base = "http://127.0.0.1:$ManagerPort"
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
# Deliberately public fixture token, used only in this newly-created test volume.
$fixtureToken = 'a' * 64
$checks = [Collections.Generic.List[string]]::new()
$created = $false
$complete = $false

function Invoke-Docker([string[]]$Arguments) {
    $result = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Docker $($Arguments[0]) failed: $result" }
    return $result
}
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Pass([string]$Message) { $checks.Add($Message); Write-Host "PASS $Message" }
function Api([string]$Path, [string]$Method = 'GET', $Body = $null, [int]$Expected = 200,
    [switch]$Anonymous, [string]$InputFile, [string]$OutFile, [hashtable]$Headers = @{}) {
    # PowerShell's WebSession retains custom request headers; restore Host after
    # the deliberately forged-Host check rather than contaminating later calls.
    $headersCopy = @{ 'X-Manager-Request' = '1'; Origin = $base; Host = "127.0.0.1:$ManagerPort" }
    foreach ($key in $Headers.Keys) { $headersCopy[$key] = $Headers[$key] }
    $request = @{ Uri = "$base$Path"; Method = $Method; SkipHttpErrorCheck = $true; TimeoutSec = 270; Headers = $headersCopy }
    if (!$Anonymous) { $request.WebSession = $session }
    if ($null -ne $Body) { $request.Body = $Body | ConvertTo-Json -Depth 10 -Compress; $request.ContentType = 'application/json' }
    if ($InputFile) { $request.InFile = $InputFile; $request.ContentType = 'application/octet-stream' }
    if ($OutFile) { $request.OutFile = $OutFile; $request.PassThru = $true }
    $response = Invoke-WebRequest @request
    if ([int]$response.StatusCode -ne $Expected) { throw "$Method $Path expected $Expected, received $([int]$response.StatusCode): $($response.Content)" }
    if (!$OutFile -and $response.Content -and $response.Headers['Content-Type'] -match 'application/json') { return $response.Content | ConvertFrom-Json }
    return $response
}
function Wait-Manager {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        try { $health = Invoke-RestMethod "$base/api/health" -TimeoutSec 2; if ($health.application -eq 'coi-dedicated-manager') { return } } catch { }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Manager did not start.'
}
function Wait-Server([string[]]$States, [int]$Seconds = 180) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do {
        $state = Api "/api/servers/$script:profileId"
        if ($state.state -in $States) { return $state }
        if ($state.state -eq 'faulted') { throw "Server faulted: $($state.lastError)" }
        Start-Sleep -Milliseconds 400
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Expected $States, received $($state.state): $($state.lastError)"
}
function Save-Evidence {
    [ordered]@{ Container = $name; Volume = $volume; Image = $Image; ManagerPort = $ManagerPort; GamePort = $GamePort;
        Completed = $complete; Checks = $checks.ToArray() } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $work 'results.json')
}
try {
    $sourceHash = (Get-FileHash -LiteralPath $SaveFile).Hash
    [void](Invoke-Docker @('volume', 'create', $volume))
    $seed = "$name-seed"
    [void](Invoke-Docker @('create', '--name', $seed, '--network', 'none', '--user', '0', '--mount', "type=volume,src=$volume,dst=/data",
        '--entrypoint', 'sh', $Image, '-c', 'mkdir -p /data/.manager; printf %s aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > /data/.manager/admin.key; printf %s bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb > /data/join.key; printf %s cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc > /data/control.key; chown -R 1654:1654 /data; chmod 700 /data /data/.manager; chmod 600 /data/*.key /data/.manager/admin.key'))
    [void](Invoke-Docker @('cp', $SaveFile, "${seed}:/data/island.save"))
    [void](Invoke-Docker @('start', '-a', $seed))
    [void](Invoke-Docker @('rm', $seed))
    [void](Invoke-Docker @('run', '-d', '--name', $name, '--init', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true',
        '--tmpfs', '/tmp:rw,noexec,nosuid,size=512m', '--mount', "type=volume,src=$volume,dst=/data",
        '--mount', "type=bind,src=$GameDir,dst=/game,readonly", '-p', "127.0.0.1:${ManagerPort}:28560", '-p', "127.0.0.1:${GamePort}:28551",
        '-e', "COI_PUBLIC_GAME_PORT=$GamePort", $Image))
    $created = $true
    Save-Evidence
    Wait-Manager
    [void](Api '/api/state' -Expected 401 -Anonymous)
    [void](Api '/api/auth/login' -Method POST -Body @{token=('d' * 64)} -Expected 401)
    [void](Api '/api/auth/login' -Method POST -Body @{token=$fixtureToken})
    $all = Api '/api/state'
    Assert ($all.profiles.Count -eq 1) 'Expected one adopted container profile.'
    $profile = $all.profiles[0]
    $script:profileId = $profile.id
    Assert ($profile.dataDirectory -eq '/data') 'Existing island was not adopted in place.'
    Assert ($all.environment.publicGamePort -eq $GamePort) 'Advertised player port did not match mapping.'
    [void](Api "/api/servers/$profileId/join-key" -Expected 401 -Anonymous)
    [void](Api "/api/servers/$profileId/logs/download" -Expected 401 -Anonymous)
    [void](Api '/api/state' -Expected 403 -Headers @{Host="attacker.invalid:$ManagerPort"})
    [void](Api "/api/servers/$profileId/actions/save" -Method POST -Expected 403 -Headers @{Origin='http://attacker.invalid'})
    Pass 'LAN authentication protects state, actions and private downloads; adopted existing /data profile'
    $running = Wait-Server @('running')
    Assert ($running.world.Population -gt 0) 'Copied island has no population.'
    [void](Api "/api/servers/$profileId/join-key" -OutFile (Join-Path $work 'join.key'))
    Assert ((Get-Content (Join-Path $work 'join.key') -Raw).Trim() -eq ('b' * 64)) 'Existing join key changed.'
    [void](Api "/api/servers/$profileId/actions/pause" -Method POST)
    $paused = Wait-Server @('paused')
    Start-Sleep -Seconds 3
    $still = Api "/api/servers/$profileId"
    Assert ($still.step -eq $paused.step) 'Paused island advanced.'
    [void](Api "/api/servers/$profileId/actions/save" -Method POST)
    $backup = Api "/api/servers/$profileId/backups" -Method POST -Body @{name='Container lifecycle backup'}
    $download = Join-Path $work 'backup.save'
    [void](Api "/api/servers/$profileId/backups/$($backup.id)/download" -OutFile $download)
    Assert ((Get-FileHash $download).Hash.ToLowerInvariant() -eq $backup.sha256) 'Downloaded backup checksum mismatch.'
    [void](Api "/api/servers/$profileId/backups/$($backup.id)/restore" -Method POST -Expected 409)
    [void](Api "/api/servers/$profileId/saves/import?name=blocked" -Method POST -InputFile $SaveFile -Expected 409)
    [void](Api "/api/servers/$profileId/actions/resume" -Method POST)
    [void](Wait-Server @('running'))
    $logs = Api "/api/servers/$profileId/logs"
    Assert ($logs.entries.Count -gt 0) 'Live logs missing.'
    Pass 'Real island pause/resume/save/logs and checksum-verified backup work; live save replacement blocked'
    [void](Api "/api/servers/$profileId/actions/stop" -Method POST)
    [void](Wait-Server @('stopped'))
    [void](Invoke-Docker @('exec', $name, '/app/healthcheck.sh'))
    $imported = Api "/api/servers/$profileId/saves/import?name=Fixture" -Method POST -InputFile $SaveFile
    Assert ($imported.sha256 -eq $sourceHash.ToLowerInvariant()) 'Imported fixture checksum mismatch.'
    $restored = Api "/api/servers/$profileId/backups/$($imported.id)/restore" -Method POST
    Assert ($null -ne $restored.previousBackup) 'Restore did not protect previous island.'
    [void](Invoke-Docker @('cp', "${name}:/data/island.save", (Join-Path $work 'restored.save')))
    Assert ((Get-FileHash (Join-Path $work 'restored.save')).Hash -eq $sourceHash) 'Restore differs from imported fixture.'
    $settings = @{Name='Managed container island';GameDirectory=$profile.gameDirectory;Map=$profile.map;BindAddress=$profile.bindAddress;
        ControlPort=$profile.controlPort;GamePort=$profile.gamePort;AutosaveSeconds=45;StartingFactory=$profile.startingFactory}
    [void](Api "/api/servers/$profileId" -Method PUT -Body $settings)
    [void](Invoke-Docker @('restart', '--timeout', '300', $name))
    Wait-Manager
    $after = Api '/api/state'
    Assert ($after.profiles[0].name -eq 'Managed container island' -and $after.profiles[0].autosaveSeconds -eq 45) 'Settings did not persist.'
    [void](Wait-Server @('stopped'))
    [void](Invoke-Docker @('exec', $name, '/app/healthcheck.sh'))
    Pass 'Import/restore preserves prior island; browser session, settings and deliberate stopped state survive container restart'
    [void](Api "/api/servers/$profileId/actions/start" -Method POST)
    [void](Wait-Server @('running'))
    [void](Api "/api/servers/$profileId/actions/restart" -Method POST)
    [void](Wait-Server @('running'))
    [void](Api "/api/servers/$profileId/actions/pause" -Method POST)
    $saved = Wait-Server @('paused')
    [void](Api "/api/servers/$profileId/actions/save" -Method POST)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    [void](Invoke-Docker @('stop', '--timeout', '300', $name))
    $watch.Stop()
    $state = (Invoke-Docker @('inspect', '--format', '{{json .State}}', $name) | ConvertFrom-Json)
    Assert ($state.ExitCode -eq 0 -and !$state.Running) 'Container shutdown did not finish cleanly.'
    [void](Invoke-Docker @('start', $name))
    Wait-Manager
    $restarted = Wait-Server @('running')
    Assert ($restarted.world.Population -eq $saved.world.Population) 'Population changed across paused save/restart.'
    Assert ($restarted.step -ge $saved.step) 'Restart regressed simulation state.'
    $serverLog = Join-Path $work 'server.log'
    [void](Api "/api/servers/$profileId/logs/download" -OutFile $serverLog)
    Assert ((Get-Content -LiteralPath $serverLog -Raw).Contains("Initialized at simulation step $($saved.step)")) 'Restart did not load exact saved step.'
    [void](Api "/api/servers/$profileId/join-key" -OutFile (Join-Path $work 'join-after.key'))
    Assert ((Get-FileHash (Join-Path $work 'join.key')).Hash -eq (Get-FileHash (Join-Path $work 'join-after.key')).Hash) 'Join key changed across restart.'
    Pass "Dashboard restart and Docker SIGTERM save/restart pass; clean stop in $([Math]::Round($watch.Elapsed.TotalSeconds,2))s; keys and island preserved"
    [void](Api '/api/auth/logout' -Method POST -Body @{})
    [void](Api '/api/state' -Expected 401)
    [void](Api '/api/auth/login' -Method POST -Body @{token=$fixtureToken})
    [void](Api "/api/servers/$profileId/actions/stop" -Method POST)
    [void](Wait-Server @('stopped'))
    Assert ((Get-FileHash -LiteralPath $SaveFile).Hash -eq $sourceHash) 'Source fixture changed.'
    $complete = $true
    Pass 'Logout revokes browser access and original source fixture remains unchanged'
}
finally {
    if ($created) {
        & docker logs $name *> (Join-Path $work 'container.log')
        if (!$KeepRunning -or !$complete) { & docker stop --timeout 300 $name *> $null }
    }
    Save-Evidence
    Write-Host "Evidence: $work"
}
