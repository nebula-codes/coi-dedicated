param(
    [ValidateSet('status', 'pause', 'resume', 'save', 'snapshot', 'shutdown')][string]$Command = 'status',
    [string]$HostName = '127.0.0.1',
    [int]$Port = 28550,
    [string]$KeyFile,
    [string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $KeyFile) { $KeyFile = Join-Path $projectRoot 'server-data\control.key' }
$controlDll = Join-Path $projectRoot 'src\CoiDedicated.Control\bin\Release\net10.0\CoiDedicated.Control.dll'
$controlArguments = @($controlDll, '--host', $HostName, '--port', "$Port", '--secret-file', $KeyFile, '--command', $Command)
if ($OutputFile) { $controlArguments += @('--output', $OutputFile) }
& dotnet @controlArguments
exit $LASTEXITCODE
