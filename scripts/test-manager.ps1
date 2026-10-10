#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$SaveFile,
    [int]$ManagerPort = 28660,
    [int]$ControlPort = 28650,
    [int]$GamePort = 28651
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hostDll = Join-Path $repository 'artifacts\manager-host\CoiDedicated.dll'
$managerBuild = Join-Path $repository 'src\CoiDedicated.Manager\bin\Release\net10.0'
$managerDll = Join-Path $managerBuild 'CoiDedicated.Manager.dll'
if (-not $SaveFile) { $SaveFile = Join-Path $repository '.work\final-verification\island.save' }
$SaveFile = [IO.Path]::GetFullPath($SaveFile)
foreach ($required in @($hostDll, $managerDll, $SaveFile)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required integration fixture or build missing: $required" }
}
if (@($ManagerPort, $ControlPort, $GamePort | Select-Object -Unique).Count -ne 3) { throw 'Integration ports must be distinct.' }
foreach ($port in @($ManagerPort, $ControlPort, $GamePort)) {
    if ($port -lt 1024 -or $port -gt 65535) { throw 'Integration ports must be from 1024 to 65535.' }
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    $listener.ExclusiveAddressUse = $true
    try { $listener.Start() } finally { $listener.Stop() }
}

$workRoot = Join-Path $repository ('.work\manager-integration-' + [Guid]::NewGuid().ToString('N'))
$dataRoot = Join-Path $workRoot 'manager-data'
[void][IO.Directory]::CreateDirectory($workRoot)
$managerBin = Join-Path $workRoot 'manager-bin'
[void][IO.Directory]::CreateDirectory($managerBin)
# Keep the developer build writable while this integration manager is running.
Get-ChildItem -LiteralPath $managerBuild -File | Copy-Item -Destination $managerBin
$managerDll = Join-Path $managerBin 'CoiDedicated.Manager.dll'
$baseUrl = "http://127.0.0.1:$ManagerPort"
$managerProcess = $null
$serverProcessId = $null
$profileId = $null
$launchCount = 0
$checks = [Collections.Generic.List[string]]::new()

function Assert-That([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Pass([string]$Message) {
    $checks.Add($Message)
    Write-Host "PASS $Message"
}

function Invoke-Api {
    param([string]$Path, [string]$Method = 'Get', $Body, [int]$Expected = 200,
        [hashtable]$Headers = @{'X-Manager-Request' = '1'}, [string]$InputFile)
    $request = @{ Uri = $baseUrl + $Path; Method = $Method; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 150 }
    if ($InputFile) { $request.InFile = $InputFile; $request.ContentType = 'application/octet-stream' }
    elseif ($null -ne $Body) { $request.Body = $Body | ConvertTo-Json -Depth 12 -Compress; $request.ContentType = 'application/json' }
    $response = Invoke-WebRequest @request
    if ([int]$response.StatusCode -ne $Expected) {
        throw "Expected HTTP $Expected for $Method $Path, got $([int]$response.StatusCode): $($response.Content)"
    }
    if ($response.Content -and $response.Headers['Content-Type'] -match 'application/json') { return $response.Content | ConvertFrom-Json }
    return $response
}

function Start-TestManager {
    $script:launchCount++
    # Direct dotnet launch; quote paths for Windows' command-line parsing without invoking a shell.
    $arguments = @($managerDll, '--repository', $repository, '--data-dir', $dataRoot, '--host-dll', $hostDll, '--port', "$ManagerPort")
    $quoted = ($arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $script:managerProcess = Start-Process -FilePath 'dotnet' -ArgumentList $quoted -WorkingDirectory $repository -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $workRoot "manager-$launchCount.log") -RedirectStandardError (Join-Path $workRoot "manager-$launchCount.error.log")
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        if ($managerProcess.HasExited) { throw "Integration manager exited with code $($managerProcess.ExitCode). See $workRoot" }
        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/api/health" -TimeoutSec 2
            if ($health.application -eq 'coi-dedicated-manager') { return }
        } catch { }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Integration manager did not become ready within 30 seconds.'
}

function Wait-Server([string[]]$States, [int]$Seconds = 90) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do {
        $status = Invoke-Api "/api/servers/$profileId"
        if ($States -contains $status.state) { return $status }
        if ($status.state -eq 'faulted') { throw "Integration server faulted: $($status.lastError)" }
        Start-Sleep -Milliseconds 400
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Expected state $($States -join '/') within $Seconds seconds; received $($status.state): $($status.lastError)"
}

function Stop-OwnManager {
    if ($managerProcess -and -not $managerProcess.HasExited) {
        # The handle came from this script's Start-Process; never enumerate or terminate other servers.
        $managerProcess.Kill()
        if (-not $managerProcess.WaitForExit(10000)) { throw 'Could not stop this test manager process.' }
        $managerProcess.Dispose()
        $script:managerProcess = $null
    }
}

try {
    Start-TestManager
    $state = Invoke-Api '/api/state'
    $profile = $state.profiles[0]
    $profileId = $profile.id
    Assert-That ($state.profiles.Count -eq 1) 'Expected one default profile in isolated manager data.'
    [void](Invoke-Api "/api/servers/$profileId/actions/start" -Method Post -Expected 403 -Headers @{})
    [void](Invoke-Api "/api/servers/$profileId/actions/start" -Method Post -Expected 403 -Headers @{'X-Manager-Request'='1'; Origin='http://other.example'})
    [void](Invoke-Api '/api/state' -Expected 403 -Headers @{Host="other.example:$ManagerPort"})
    [void](Invoke-Api '/api/state' -Expected 403 -Headers @{'Sec-Fetch-Site'='cross-site'})
    Pass 'Local API rejects missing action header, wrong origin, foreign Host, and cross-site requests'

    $settings = @{ Name='Manager integration island'; GameDirectory=$profile.gameDirectory; Map=$profile.map; BindAddress='127.0.0.1';
        ControlPort=$ControlPort; GamePort=$GamePort; AutosaveSeconds=60; StartingFactory=$true }
    $profile = Invoke-Api "/api/servers/$profileId" -Method Put -Body $settings
    Assert-That ($profile.controlPort -eq $ControlPort -and $profile.gamePort -eq $GamePort) 'Profile ports were not persisted.'
    $imported = Invoke-Api "/api/servers/$profileId/saves/import?name=Integration%20seed" -Method Post -InputFile $SaveFile
    Assert-That ($imported.sha256 -eq (Get-FileHash -LiteralPath $SaveFile -Algorithm SHA256).Hash.ToLowerInvariant()) 'Imported save hash differs.'
    [void](Invoke-Api "/api/servers/$profileId/backups/$($imported.id)/restore" -Method Post)
    $currentPath = Join-Path $profile.dataDirectory 'island.save'
    Assert-That ((Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $imported.sha256) 'Seed restore differs from original.'
    Pass 'Profile settings, bounded save import, and stopped restore work'

    [void](Invoke-Api "/api/servers/$profileId/actions/start" -Method Post)
    $running = Wait-Server @('running')
    $serverProcessId = $running.processId
    Assert-That ($running.isOwned -and $serverProcessId -gt 0 -and $null -ne $running.step) 'Server did not prove authenticated readiness.'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 400
        $advanced = Invoke-Api "/api/servers/$profileId"
    } while ($advanced.step -le $running.step -and [DateTimeOffset]::UtcNow -lt $deadline)
    Assert-That ($advanced.step -gt $running.step) 'Unattended simulation did not advance.'
    Pass 'Real host reaches authenticated readiness and simulates with no players'

    [void](Invoke-Api "/api/servers/$profileId/actions/pause" -Method Post)
    $paused = Wait-Server @('paused')
    Start-Sleep -Seconds 3
    $stillPaused = Invoke-Api "/api/servers/$profileId"
    Assert-That ($stillPaused.state -eq 'paused' -and $stillPaused.step -eq $paused.step) 'Paused simulation unexpectedly advanced.'
    [void](Invoke-Api "/api/servers/$profileId/actions/resume" -Method Post)
    [void](Wait-Server @('running'))
    [void](Invoke-Api "/api/servers/$profileId/actions/save" -Method Post)
    $backup = Invoke-Api "/api/servers/$profileId/backups" -Method Post -Body @{name='Live integration backup'}
    $download = Join-Path $workRoot 'downloaded-backup.save'
    Invoke-WebRequest -Uri "$baseUrl/api/servers/$profileId/backups/$($backup.id)/download" -OutFile $download
    Assert-That ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -eq $backup.sha256) 'Downloaded backup hash differs.'
    Pass 'Pause, resume, save, live snapshot backup, and archive download work'

    [void](Invoke-Api "/api/servers/$profileId/backups/$($imported.id)/restore" -Method Post -Expected 409)
    [void](Invoke-Api "/api/servers/$profileId" -Method Put -Body $settings -Expected 409)
    [void](Invoke-Api "/api/servers/$profileId/saves/import?name=Blocked" -Method Post -InputFile $SaveFile -Expected 409)
    Pass 'Running server rejects restore, settings changes, and imports'

    Stop-OwnManager
    $surviving = Get-Process -Id $serverProcessId -ErrorAction Stop
    Assert-That (-not $surviving.HasExited) 'Server exited when its manager closed.'
    $surviving.Dispose()
    Start-TestManager
    $reattached = Wait-Server @('running')
    Assert-That ($reattached.processId -eq $serverProcessId -and $reattached.isOwned) 'Manager did not reattach to the original server process.'
    Pass 'Server survives manager termination and the reopened manager reattaches to its exact PID'

    [void](Invoke-Api "/api/servers/$profileId/actions/stop" -Method Post)
    $stopped = Wait-Server @('stopped')
    Assert-That ($null -eq $stopped.processId -and $stopped.exitCode -eq 0) 'Server did not stop cleanly.'
    Assert-That ((Get-Item -LiteralPath $currentPath).Length -gt 40) 'Final save is missing.'
    $restored = Invoke-Api "/api/servers/$profileId/backups/$($imported.id)/restore" -Method Post
    Assert-That ($null -ne $restored.previousBackup) 'Restore did not archive the final island.'
    Assert-That ((Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $imported.sha256) 'Post-stop restore differs from initial backup.'
    Pass 'Graceful stop writes the final save and restore preserves it in the backup library'
    Write-Host "$($checks.Count)/$($checks.Count) manager HTTP integration checks passed. Evidence: $workRoot"
}
finally {
    if ($profileId) {
        try {
            if (-not $managerProcess -or $managerProcess.HasExited) { Start-TestManager }
            [void](Invoke-Api "/api/servers/$profileId/actions/stop" -Method Post)
        } catch { Write-Warning "Could not gracefully stop integration server PID $serverProcessId. Its isolated data remains at $dataRoot. $($_.Exception.Message)" }
    }
    Stop-OwnManager
}
