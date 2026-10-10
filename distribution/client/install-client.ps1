#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$GameDir,
    [switch]$NonInteractive,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$pluginFiles = @(
    'BepInEx/plugins/CoiDedicated/CoiDedicated.Client.dll',
    'BepInEx/plugins/CoiDedicated/CoiDedicated.Protocol.dll',
    'BepInEx/plugins/CoiDedicated/CoiDedicated.GameAdapter.dll'
)
$requiredPayload = $pluginFiles + @('BepInEx/core/BepInEx.dll', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version')
$recordRelative = 'BepInEx/CoiDedicatedInstaller/installed.json'

function Get-RelativePathChecked([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 240 -or [IO.Path]::IsPathRooted($Value) -or $Value.Contains(':')) {
        throw "Unsafe package path: $Value"
    }
    $normalized = $Value.Replace('\', '/')
    foreach ($part in $normalized.Split('/')) {
        if ($part.Length -eq 0 -or $part -in @('.', '..') -or $part -ne $part.TrimEnd(' ', '.') -or
            $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw "Unsafe package path: $Value"
        }
    }
    return $normalized
}

function Get-SafeDestination([string]$Root, [string]$Relative) {
    $relative = Get-RelativePathChecked $Relative
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $candidate = [IO.Path]::GetFullPath((Join-Path $rootFull $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
    if (-not $candidate.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes its destination: $Relative"
    }
    $walk = $rootFull
    foreach ($part in $relative.Split('/')) {
        $walk = Join-Path $walk $part
        if (Test-Path -LiteralPath $walk) {
            $entry = Get-Item -LiteralPath $walk -Force
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Symbolic links and junctions are not supported inside the installation paths: $walk" }
            if (-not [string]::Equals($walk, $candidate, [StringComparison]::OrdinalIgnoreCase) -and -not $entry.PSIsContainer) { throw "A file blocks an installation folder: $walk" }
        }
    }
    return $candidate
}

function Get-Sha([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Find-SteamGames {
    $roots = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($registry in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        $value = Get-ItemProperty -LiteralPath $registry -ErrorAction SilentlyContinue
        if ($null -ne $value) {
            foreach ($property in @('SteamPath', 'InstallPath')) {
                if ($null -ne $value.PSObject.Properties[$property] -and $value.$property) { [void]$roots.Add([string]$value.$property) }
            }
        }
    }
    if (${env:ProgramFiles(x86)}) { [void]$roots.Add((Join-Path ${env:ProgramFiles(x86)} 'Steam')) }
    if ($env:ProgramFiles) { [void]$roots.Add((Join-Path $env:ProgramFiles 'Steam')) }
    $libraries = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $roots) {
        [void]$libraries.Add($root)
        $folders = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $folders -PathType Leaf) {
            $contents = Get-Content -LiteralPath $folders -Raw
            foreach ($match in [regex]::Matches($contents, '"(?:path|\d+)"\s*"([^"\r\n]+)"')) {
                $path = $match.Groups[1].Value.Replace('\\', '\')
                if ([IO.Path]::IsPathRooted($path)) { [void]$libraries.Add($path) }
            }
        }
    }
    $found = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($library in $libraries) {
        $steamapps = Join-Path $library 'steamapps'
        $manifest = Join-Path $steamapps 'appmanifest_1594320.acf'
        if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { continue }
        $contents = Get-Content -LiteralPath $manifest -Raw
        $match = [regex]::Match($contents, '"installdir"\s*"([^"\r\n]+)"')
        if ($match.Success) {
            $relative = $match.Groups[1].Value
            if ($relative -match '^[^\\/:]+$' -and $relative -notin @('.', '..')) {
                $candidate = Join-Path (Join-Path $steamapps 'common') $relative
                if (Test-Path -LiteralPath (Join-Path $candidate 'Captain of Industry.exe') -PathType Leaf) { [void]$found.Add([IO.Path]::GetFullPath($candidate)) }
            }
        }
    }
    return @($found | Sort-Object)
}

function Select-GameDirectory([string]$Requested) {
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return $Requested }
    $candidates = @(Find-SteamGames)
    if ($candidates.Count -eq 1) { return $candidates[0] }
    if ($NonInteractive) { throw 'Specify -GameDir: the installer could not identify exactly one installed Captain of Industry copy.' }
    if ($candidates.Count -gt 1) {
        Write-Host 'Captain of Industry installations:'
        for ($i = 0; $i -lt $candidates.Count; $i++) { Write-Host ('  [{0}] {1}' -f ($i + 1), $candidates[$i]) }
    }
    Write-Host 'Enter the game folder, a listed number, or B to browse. Leave empty to cancel.'
    $answer = (Read-Host 'Game folder').Trim().Trim('"')
    if ($answer.Length -eq 0) { throw 'Installation cancelled.' }
    if ($answer -eq 'B') {
        Add-Type -AssemblyName System.Windows.Forms
        $browser = New-Object System.Windows.Forms.FolderBrowserDialog
        try {
            $browser.Description = 'Select the Captain of Industry folder containing Captain of Industry.exe'
            $browser.ShowNewFolderButton = $false
            if ($browser.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Installation cancelled.' }
            return $browser.SelectedPath
        } finally { $browser.Dispose() }
    }
    $selected = 0
    if ([int]::TryParse($answer, [ref]$selected) -and $selected -ge 1 -and $selected -le $candidates.Count) { return $candidates[$selected - 1] }
    return $answer
}

function Assert-GameDirectory([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Game directory was not found: $Directory" }
    $resolved = (Resolve-Path -LiteralPath $Directory).ProviderPath.TrimEnd('\', '/')
    foreach ($relative in @('Captain of Industry.exe', 'UnityPlayer.dll', 'Captain of Industry_Data/Managed/Mafi.dll', 'Captain of Industry_Data/Managed/Mafi.Core.dll', 'Captain of Industry_Data/Managed/Mafi.Base.dll', 'Captain of Industry_Data/Managed/Mafi.Unity.dll', 'Captain of Industry_Data/Managed/UnityEngine.CoreModule.dll')) {
        $path = Get-SafeDestination $resolved $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "This does not look like a complete Captain of Industry installation. Missing game file: $relative" }
    }
    $executable = Join-Path $resolved 'Captain of Industry.exe'
    foreach ($process in @(Get-Process -Name 'Captain of Industry' -ErrorAction SilentlyContinue)) {
        try { $path = $process.Path } catch { throw 'Close Captain of Industry before installing or removing its client plugin.' }
        if ([string]::IsNullOrWhiteSpace($path) -or [string]::Equals($path, $executable, [StringComparison]::OrdinalIgnoreCase)) { throw 'Close Captain of Industry before installing or removing its client plugin.' }
    }
    return $resolved
}

function Read-Payload {
    $manifest = Join-Path $PSScriptRoot 'files.json'
    $payload = Join-Path $PSScriptRoot 'payload'
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf) -or -not (Test-Path -LiteralPath $payload -PathType Container)) { throw 'The package is incomplete. Extract the entire player ZIP before running Install Client.cmd.' }
    if (((Get-Item -LiteralPath $payload -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'The package payload cannot be a symbolic link or junction.' }
    $raw = Get-Content -LiteralPath $manifest -Raw
    if (-not $raw.TrimStart().StartsWith('[')) { throw 'Invalid files.json: expected an array of payload files.' }
    $entries = ConvertFrom-Json -InputObject $raw
    if ($entries.Count -lt $requiredPayload.Count -or $entries.Count -gt 100) { throw 'Invalid package manifest file count.' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $verified = New-Object 'System.Collections.Generic.List[object]'
    foreach ($entry in $entries) {
        if ($null -eq $entry -or $null -eq $entry.PSObject.Properties['path'] -or $null -eq $entry.PSObject.Properties['sha256']) { throw 'Each files.json entry must contain path and sha256.' }
        $relative = Get-RelativePathChecked ([string]$entry.path)
        if (-not $seen.Add($relative)) { throw "Duplicate package path: $relative" }
        $plugin = $relative -in $pluginFiles
        if (-not $plugin -and $relative -notin @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version') -and $relative -notmatch '^BepInEx/core/[^/]+\.(dll|xml)$') { throw "Unexpected payload location: $relative" }
        if ($relative -match '(?i)(?:^|/)(?:Mafi(?:\.[^/]*)?|Unity[^/]*)\.dll$') { throw "Game assemblies must not be included in the player package: $relative" }
        $hash = [string]$entry.sha256
        if ($hash -notmatch '^[0-9a-fA-F]{64}$') { throw "Invalid SHA-256 for package file: $relative" }
        $source = Get-SafeDestination $payload $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing package file: $relative" }
        if ((Get-Sha $source) -ne $hash.ToLowerInvariant()) { throw "Package checksum mismatch: $relative. Download and extract the release again." }
        $verified.Add([pscustomobject]@{ Relative = $relative; Source = $source; SHA256 = $hash.ToLowerInvariant(); Plugin = $plugin })
    }
    foreach ($relative in $requiredPayload) { if (-not $seen.Contains($relative)) { throw "Required file is missing from files.json: $relative" } }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($payload)
    while ($pending.Count -gt 0) {
        foreach ($child in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Package contains a symbolic link or junction: $($child.Name)" }
            if ($child.PSIsContainer) { $pending.Push($child.FullName) }
            else {
                $relative = $child.FullName.Substring($payload.Length).TrimStart('\', '/').Replace('\', '/')
                if (-not $seen.Contains($relative)) { throw "Unlisted payload file: $relative" }
            }
        }
    }
    return $verified.ToArray()
}

function Install-Client([string]$Directory, [object[]]$Payload) {
    # Resolve and inspect every destination before any installation writes occur.
    $plan = New-Object 'System.Collections.Generic.List[object]'
    $recordPath = Get-SafeDestination $Directory $recordRelative
    if (Test-Path -LiteralPath $recordPath -PathType Container) { throw "Installation record path is a directory: $recordPath" }
    foreach ($item in $Payload) {
        $target = Get-SafeDestination $Directory $item.Relative
        $exists = Test-Path -LiteralPath $target
        if ($exists -and -not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "A directory blocks an installation file: $target" }
        $originalHash = if ($exists) { Get-Sha $target } else { $null }
        $changed = $originalHash -ne $item.SHA256
        if ($exists -and $changed -and -not $item.Plugin) { throw "Existing loader file differs; no files were changed: $($item.Relative). Keep your current loader or use a clean compatible game installation." }
        $parent = Split-Path $target -Parent
        while ($parent.Length -gt $Directory.Length) {
            if ((Test-Path -LiteralPath $parent) -and -not (Test-Path -LiteralPath $parent -PathType Container)) { throw "A file blocks an installation folder: $parent" }
            $parent = Split-Path $parent -Parent
        }
        $plan.Add([pscustomobject]@{ Item = $item; Target = $target; Existed = $exists; Changed = $changed; OriginalHash = $originalHash; Backup = $null; Written = $false })
    }
    $transaction = 'BepInEx/CoiDedicatedInstaller/backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    foreach ($step in $plan) {
        if ($step.Existed -and $step.Changed) { $step.Backup = Get-SafeDestination $Directory ($transaction + '/' + $step.Item.Relative) }
    }
    $recordBackup = $null
    try {
        # Save previous versions before touching any plugin DLL.
        foreach ($step in $plan) {
            if ($null -ne $step.Backup) { New-Item -ItemType Directory -Path (Split-Path $step.Backup -Parent) -Force | Out-Null; Copy-Item -LiteralPath $step.Target -Destination $step.Backup }
        }
        if (Test-Path -LiteralPath $recordPath -PathType Leaf) {
            $recordBackup = Get-SafeDestination $Directory ($transaction + '/installed.json')
            New-Item -ItemType Directory -Path (Split-Path $recordBackup -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $recordPath -Destination $recordBackup
        }
        foreach ($step in $plan) {
            if (-not $step.Changed) { continue }
            New-Item -ItemType Directory -Path (Split-Path $step.Target -Parent) -Force | Out-Null
            $step.Written = $true
            Copy-Item -LiteralPath $step.Item.Source -Destination $step.Target -Force
            if ((Get-Sha $step.Target) -ne $step.Item.SHA256) { throw "Installed file failed verification: $($step.Item.Relative)" }
        }
        $records = @($plan | ForEach-Object { [pscustomobject]@{ path = $_.Item.Relative; sha256 = $_.Item.SHA256; plugin = $_.Item.Plugin } })
        $record = [pscustomobject]@{ formatVersion = 1; installedUtc = [DateTime]::UtcNow.ToString('o'); files = $records }
        New-Item -ItemType Directory -Path (Split-Path $recordPath -Parent) -Force | Out-Null
        $temporary = $recordPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
        $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporary -Encoding UTF8
        Move-Item -LiteralPath $temporary -Destination $recordPath -Force
    } catch {
        $failure = $_
        foreach ($step in $plan) {
            if (-not $step.Written) { continue }
            try {
                if ($step.Existed -and $null -ne $step.Backup) { Copy-Item -LiteralPath $step.Backup -Destination $step.Target -Force }
                elseif (-not $step.Existed -and (Test-Path -LiteralPath $step.Target -PathType Leaf)) { Remove-Item -LiteralPath $step.Target }
            } catch { Write-Warning "Automatic rollback could not restore $($step.Target). Previous plugin copies are in $transaction." }
        }
        if ($null -ne $recordBackup) { try { Copy-Item -LiteralPath $recordBackup -Destination $recordPath -Force } catch { Write-Warning 'The previous installation record could not be restored.' } }
        throw $failure
    }
    Write-Host ''
    Write-Host 'Dedicated client installed successfully.' -ForegroundColor Green
    Write-Host "Game folder: $Directory"
    Write-Host 'Launch Captain of Industry normally, then press F8 at the main menu.'
    Write-Host 'Enter the server address, game port, and the join.key supplied privately by its host.'
    if (@($plan | Where-Object { $null -ne $_.Backup }).Count -gt 0) { Write-Host "Previous plugin files were preserved in: $(Join-Path $Directory $transaction)" }
}

function Uninstall-Client([string]$Directory) {
    $recordPath = Get-SafeDestination $Directory $recordRelative
    if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) { throw 'No installation record was found. This uninstaller only removes files recorded by this installer.' }
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($null -eq $record -or $null -eq $record.PSObject.Properties['formatVersion'] -or $record.formatVersion -ne 1 -or $null -eq $record.PSObject.Properties['files']) { throw 'Invalid installation record. No files were removed.' }
    $removals = New-Object 'System.Collections.Generic.List[object]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @($record.files)) {
        if ($null -eq $item -or $null -eq $item.PSObject.Properties['path'] -or $null -eq $item.PSObject.Properties['sha256']) { throw 'Invalid installation record. No files were removed.' }
        $relative = Get-RelativePathChecked ([string]$item.path)
        if (-not $seen.Add($relative)) { throw 'Duplicate path in installation record. No files were removed.' }
        # Shared BepInEx/Doorstop files are deliberately retained for other mods.
        if ($relative -notin $pluginFiles) { continue }
        if ([string]$item.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid checksum in installation record. No files were removed.' }
        $target = Get-SafeDestination $Directory $relative
        if (Test-Path -LiteralPath $target) {
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "A directory blocks an installed plugin path: $relative" }
            if ((Get-Sha $target) -ne ([string]$item.sha256).ToLowerInvariant()) { throw "Plugin file has changed since installation; no files were removed: $relative. Preserve or inspect it before uninstalling." }
            $removals.Add([pscustomobject]@{ Target = $target; Relative = $relative })
        }
    }
    foreach ($relative in $pluginFiles) { if (-not $seen.Contains($relative)) { throw 'Installation record does not list every dedicated plugin file. No files were removed.' } }
    $backupRelative = 'BepInEx/CoiDedicatedInstaller/backups/uninstall-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    foreach ($item in $removals) {
        $backup = Get-SafeDestination $Directory ($backupRelative + '/' + $item.Relative)
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $item.Target -Destination $backup
    }
    foreach ($item in $removals) { Remove-Item -LiteralPath $item.Target }
    Write-Host 'Dedicated client plugin removed. Shared BepInEx loader, settings, and backups were preserved.' -ForegroundColor Green
    Write-Host "Game folder: $Directory"
}

try {
    if ($env:OS -ne 'Windows_NT') { throw 'This player package installs the Windows x64 client.' }
    $payload = if (-not $Uninstall) { @(Read-Payload) } else { @() }
    $target = Assert-GameDirectory (Select-GameDirectory $GameDir)
    if ($Uninstall) { Uninstall-Client $target } else { Install-Client $target $payload }
    exit 0
} catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
