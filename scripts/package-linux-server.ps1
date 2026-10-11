#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0-alpha.6.3',
    [string]$GameDir = $env:COI_GAME_DIR,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $GameDir) { throw 'Supply -GameDir or set COI_GAME_DIR to your installed Captain of Industry folder.' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'Captain of Industry_Data/Managed/Mafi.Core.dll'))) {
    throw 'GameDir must contain Captain of Industry_Data/Managed/Mafi.Core.dll.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository "artifacts/releases/v$Version-linux" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$packageName = "CoI-Dedicated-Server-$Version-linux-x64"
$zip = Join-Path $OutputDirectory "$packageName.zip"
if (Test-Path -LiteralPath $zip) { throw "Package already exists; choose another output directory: $zip" }
$work = Join-Path $repository ('.work/linux-release-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $work $packageName
[void][IO.Directory]::CreateDirectory($package)

foreach ($project in @(
    @{ Name = 'CoiDedicated'; Output = 'server' },
    @{ Name = 'CoiDedicated.Control'; Output = 'control' },
    @{ Name = 'CoiDedicated.Manager'; Output = 'manager' }
)) {
    $arguments = @(
        'publish', (Join-Path $repository "src/$($project.Name)/$($project.Name).csproj"),
        '-c', 'Release', '-r', 'linux-x64', '--self-contained', 'false',
        '-p:UseAppHost=false', '-p:PublishSingleFile=false', '-p:DebugType=None', '-p:DebugSymbols=false',
        '-o', (Join-Path $package $project.Output), "-p:GameDir=$GameDir", "-p:Version=$Version",
        "--artifacts-path=$(Join-Path $work "build/$($project.Output)")"
    )
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $($project.Name)" }
}

# Fail closed if a project starts copying game binaries or runtime files into
# its output. This package is published publicly and must contain only our code.
$allowedNames = @(
    'CoiDedicated.dll', 'CoiDedicated.deps.json', 'CoiDedicated.runtimeconfig.json',
    'CoiDedicated.Protocol.dll', 'CoiDedicated.GameAdapter.dll',
    'CoiDedicated.Control.dll', 'CoiDedicated.Control.deps.json', 'CoiDedicated.Control.runtimeconfig.json'
)
foreach ($subdirectory in @('server', 'control')) {
    $output = Join-Path $package $subdirectory
    foreach ($file in Get-ChildItem -LiteralPath $output -File -Recurse) {
        if ($file.DirectoryName -ne $output -or $file.Name -notin $allowedNames) {
            throw "Unexpected public package file: $($file.FullName)"
        }
    }
}
# Keep the browser bundle explicit too; public packages must never collect local
# configuration, credentials, imported islands, or unrelated workspace files.
$managerOutput = Join-Path $package 'manager'
$managerNames = @('CoiDedicated.Manager.dll', 'CoiDedicated.Manager.deps.json', 'CoiDedicated.Manager.runtimeconfig.json',
    'CoiDedicated.Manager.staticwebassets.endpoints.json', 'CoiDedicated.Manager.Core.dll', 'CoiDedicated.Protocol.dll', 'web.config')
$webSource = Join-Path $repository 'src/CoiDedicated.Manager/wwwroot'
foreach ($file in Get-ChildItem -LiteralPath $managerOutput -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($managerOutput, $file.FullName).Replace('\', '/')
    if ($file.DirectoryName -eq $managerOutput -and $file.Name -in $managerNames) { continue }
    if ($relative.StartsWith('wwwroot/', [StringComparison]::Ordinal)) {
        $webName = $relative.Substring(8)
        $compressed = $file.Extension -in @('.br', '.gz')
        if ($compressed) { $webName = $webName.Substring(0, $webName.Length - $file.Extension.Length) }
        $source = Join-Path $webSource $webName
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            $expected = (Get-FileHash -LiteralPath $source).Hash
            if (!$compressed -and $expected -eq (Get-FileHash -LiteralPath $file.FullName).Hash) { continue }
            if ($compressed) {
                $inputStream = [IO.File]::OpenRead($file.FullName)
                $decoded = if ($file.Extension -eq '.br') { [IO.Compression.BrotliStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress) }
                    else { [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress) }
                try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($decoded)) }
                finally { $decoded.Dispose(); $inputStream.Dispose() }
                if ($actual -eq $expected) { continue }
            }
        }
    }
    throw "Unexpected public manager package file: $($file.FullName)"
}
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE'), (Join-Path $repository 'THIRD-PARTY-NOTICES.md') -Destination $package
$readme = Get-Content -LiteralPath (Join-Path $repository 'docs/docker-portainer.md') -Raw
$sourceRoot = "https://github.com/nebula-codes/coi-dedicated/blob/v$Version"
foreach ($file in @('compose.portainer.yml', 'compose.portainer.build.yml')) {
    $readme = $readme.Replace("(../$file)", "($sourceRoot/$file)")
}
foreach ($file in @('server-startup-validation.md', 'player-guide.md', 'multiplayer-coverage.md', 'synchronization.md')) {
    $readme = $readme.Replace("($file)", "($sourceRoot/docs/$file)")
}
$readme | Set-Content -LiteralPath (Join-Path $package 'README.md') -Encoding utf8NoBOM
[ordered]@{
    version = $Version
    platform = 'linux-x64'
    frameworkDependent = $true
    requiredRuntime = 'Microsoft.AspNetCore.App 10.0'
    browserManagerIncluded = $true
    managerAuthentication = 'private generated admin key and authenticated browser session'
    gameVersion = '0.8.7d'
    compatibility = 'dedicated-prototype-6 / cmd1'
    experimental = $true
    gameFilesIncluded = $false
    steamClientRequired = $false
    startupCompatibility = 'exact game assembly fingerprints; dedicated process only'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'release.json') -Encoding utf8NoBOM
Compress-Archive -LiteralPath $package -DestinationPath $zip -CompressionLevel Optimal
$sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$sha256  $packageName.zip" | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS-linux.txt') -Encoding ascii
Write-Host "Linux package: $zip"
Write-Host "SHA-256: $sha256"
Write-Host "Staging directory: $work"
