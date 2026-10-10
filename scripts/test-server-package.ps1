#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Package,
    [string]$SaveFile,
    [ValidateRange(1024, 65535)][int]$ManagerPort = 28670,
    [ValidateRange(1024, 65535)][int]$ControlPort = 28672,
    [ValidateRange(1024, 65535)][int]$GamePort = 28673
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Package = [IO.Path]::GetFullPath($Package)
if (-not $SaveFile) { $SaveFile = Join-Path $repository '.work\final-verification\island.save' }
$SaveFile = [IO.Path]::GetFullPath($SaveFile)
foreach ($path in @($Package, $SaveFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package or save fixture: $path" }
}
if (@(@($ManagerPort, $ControlPort, $GamePort) | Select-Object -Unique).Count -ne 3) { throw 'Test ports must be distinct.' }
foreach ($port in @($ManagerPort, $ControlPort, $GamePort)) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    $listener.ExclusiveAddressUse = $true
    try { $listener.Start() } finally { $listener.Stop() }
}

$workRoot = Join-Path $repository ('.work\package-server-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($Package, $workRoot)
$launchers = @(Get-ChildItem -LiteralPath $workRoot -Filter 'Start Server Manager.ps1' -Recurse -File)
if ($launchers.Count -ne 1) { throw 'Expected exactly one server manager launcher in the archive.' }
$packageRoot = $launchers[0].DirectoryName
$launcher = $launchers[0].FullName
$managerExe = Join-Path $packageRoot 'bin\manager\CoiDedicated.Manager.exe'
$serverExe = Join-Path $packageRoot 'bin\server\CoiDedicated.exe'
$dataRoot = Join-Path $packageRoot 'data\default'
$url = "http://127.0.0.1:$ManagerPort"
$managerProcess = $null
$profileId = $null
$serverProcessId = $null
$checks = [Collections.Generic.List[string]]::new()

function Assert-That([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Pass([string]$Message) { $checks.Add($Message); Write-Host "PASS $Message" }
function Api([string]$Path, [string]$Method = 'Get', $Body, [string]$InputFile) {
    $request = @{ Uri = $url + $Path; Method = $Method; Headers = @{'X-Manager-Request' = '1'}; TimeoutSec = 150 }
    if ($InputFile) { $request.InFile = $InputFile; $request.ContentType = 'application/octet-stream' }
    elseif ($null -ne $Body) { $request.Body = $Body | ConvertTo-Json -Depth 12 -Compress; $request.ContentType = 'application/json' }
    Invoke-RestMethod @request
}
function Start-TestManager {
    $previousPath = $env:PATH
    $previousRoot = $env:DOTNET_ROOT
    $previousLookup = $env:DOTNET_MULTILEVEL_LOOKUP
    try {
        # Child manager and host inherit an environment where dotnet cannot be found.
        $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot;$env:SystemRoot\System32\Wbem;$env:SystemRoot\System32\WindowsPowerShell\v1.0"
        $env:DOTNET_ROOT = Join-Path $workRoot 'no-external-dotnet'
        $env:DOTNET_MULTILEVEL_LOOKUP = '0'
        Assert-That (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) 'dotnet unexpectedly remains discoverable in the restricted test environment.'
        & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $launcher -NoBrowser -Port $ManagerPort
        if ($LASTEXITCODE -ne 0) { throw 'Packaged PowerShell 5.1 launcher failed.' }
    }
    finally { $env:PATH = $previousPath; $env:DOTNET_ROOT = $previousRoot; $env:DOTNET_MULTILEVEL_LOOKUP = $previousLookup }
    $matches = @(Get-Process -Name 'CoiDedicated.Manager' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $managerExe })
    Assert-That ($matches.Count -eq 1) 'Could not identify exactly one manager launched from the unique package extraction.'
    $script:managerProcess = $matches[0]
    $managerRuntime = @($managerProcess.Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' })
    Assert-That ($managerRuntime.Count -eq 1 -and $managerRuntime[0].FileName -eq (Join-Path (Split-Path $managerExe -Parent) 'coreclr.dll')) 'The packaged manager did not load its bundled .NET runtime.'
    $health = Api '/api/health'
    Assert-That ($health.application -eq 'coi-dedicated-manager') 'The packaged manager did not become healthy.'
}
function Stop-OwnManager {
    if ($managerProcess -and -not $managerProcess.HasExited) {
        Assert-That ($managerProcess.MainModule.FileName -eq $managerExe) 'Refusing to stop a manager outside this test extraction.'
        $managerProcess.Kill()
        Assert-That ($managerProcess.WaitForExit(10000)) 'This test manager did not exit.'
        $managerProcess.Dispose()
        $script:managerProcess = $null
    }
}
function Wait-Server([string[]]$States, [int]$Seconds = 150) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do {
        $status = Api "/api/servers/$profileId"
        if ($States -contains $status.state) { return $status }
        if ($status.state -eq 'faulted') { throw "Packaged server faulted: $($status.lastError)" }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Server did not reach $($States -join '/') within $Seconds seconds. Last state: $($status.state)"
}

try {
    Start-TestManager
    foreach ($asset in @('/', '/app.js', '/app.css')) {
        $response = Invoke-WebRequest -Uri ($url + $asset) -UseBasicParsing
        Assert-That ($response.StatusCode -eq 200 -and $response.Content.Length -gt 100) "Packaged dashboard asset failed: $asset"
    }
    Pass 'PowerShell 5.1 launcher, self-contained manager, and packaged dashboard assets work without dotnet on PATH'
    $state = Api '/api/state'
    Assert-That ($state.profiles.Count -eq 1) 'Expected one default profile in fresh package data.'
    $profile = $state.profiles[0]; $profileId = $profile.id
    $profile = Api "/api/servers/$profileId" Put @{
        Name='Packaged integration island'; GameDirectory=$profile.gameDirectory; Map=$profile.map; BindAddress='127.0.0.1';
        ControlPort=$ControlPort; GamePort=$GamePort; AutosaveSeconds=60; StartingFactory=$true
    }
    $imported = Api "/api/servers/$profileId/saves/import?name=Package%20test%20seed" Post -InputFile $SaveFile
    [void](Api "/api/servers/$profileId/backups/$($imported.id)/restore" Post)
    [void](Api "/api/servers/$profileId/actions/start" Post)
    $running = Wait-Server @('running')
    $serverProcessId = $running.processId
    $server = Get-Process -Id $serverProcessId
    Assert-That ($server.MainModule.FileName -eq $serverExe) 'The manager did not launch the packaged server executable.'
    $serverRuntime = @($server.Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' })
    Assert-That ($serverRuntime.Count -eq 1 -and $serverRuntime[0].FileName -eq (Join-Path (Split-Path $serverExe -Parent) 'coreclr.dll')) 'The packaged server did not load its bundled .NET runtime.'
    $server.Dispose()
    Assert-That ($running.isOwned -and $null -ne $running.step) 'Server did not authenticate readiness.'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do { Start-Sleep -Milliseconds 500; $advanced = Api "/api/servers/$profileId" } while ($advanced.step -le $running.step -and [DateTimeOffset]::UtcNow -lt $deadline)
    Assert-That ($advanced.step -gt $running.step) 'The packaged island did not advance.'
    Pass 'Packaged server EXE bootstraps the imported save and simulates using its bundled .NET runtime'
    $backup = Api "/api/servers/$profileId/backups" Post @{ name='Packaged live backup' }
    $download = Join-Path $workRoot 'downloaded-backup.save'
    Invoke-WebRequest -Uri "$url/api/servers/$profileId/backups/$($backup.id)/download" -UseBasicParsing -OutFile $download
    Assert-That ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -eq $backup.sha256) 'Downloaded live backup checksum differs.'
    Pass 'Live packaged-server backup downloads with its verified checksum'
    Stop-OwnManager
    $survivor = Get-Process -Id $serverProcessId
    Assert-That (-not $survivor.HasExited -and $survivor.MainModule.FileName -eq $serverExe) 'The packaged server exited with its manager.'
    $survivor.Dispose()
    Start-TestManager
    $reattached = Wait-Server @('running')
    Assert-That ($reattached.processId -eq $serverProcessId -and $reattached.isOwned) 'The reopened packaged manager did not reattach to the same server EXE.'
    Pass 'Real packaged server survives manager termination and reattaches with the same process identity'
    [void](Api "/api/servers/$profileId/actions/stop" Post)
    $stopped = Wait-Server @('stopped')
    $finalSave = Join-Path $profile.dataDirectory 'island.save'
    Assert-That ($stopped.exitCode -eq 0 -and $null -eq $stopped.processId -and (Get-Item -LiteralPath $finalSave).Length -gt 40) 'Packaged server did not save and exit cleanly.'
    Pass 'Graceful stop waits for the final save and zero-exit process completion'
    $evidence = @{ package=$Package; sha256=(Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant(); managerPort=$ManagerPort;
        controlPort=$ControlPort; gamePort=$GamePort; extractedRoot=$packageRoot; serverProcessId=$serverProcessId; checks=@($checks.ToArray()); finishedUtc=[DateTimeOffset]::UtcNow.ToString('O') }
    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $workRoot 'test-results.json') -Encoding UTF8
    Write-Host "$($checks.Count)/$($checks.Count) package integration checks passed. Evidence: $workRoot"
}
finally {
    if ($profileId) {
        try {
            if (-not $managerProcess -or $managerProcess.HasExited) { Start-TestManager }
            [void](Api "/api/servers/$profileId/actions/stop" Post)
        } catch { Write-Warning "Could not gracefully stop the isolated test server PID $serverProcessId. Its data remains at $dataRoot. $($_.Exception.Message)" }
    }
    Stop-OwnManager
}
