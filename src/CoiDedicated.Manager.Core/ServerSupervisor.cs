using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CoiDedicated.Manager.Core;

/// <summary>Owns one server process, authenticates readiness, and never kills a running island as a fallback.</summary>
public sealed class ServerSupervisor : IAsyncDisposable
{
    private const int LogCapacity = 2000;
    private readonly string _hostDllPath;
    private readonly string _runtimeDirectory;
    private readonly string _metadataPath;
    private readonly ControlConnection _control;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _logGate = new();
    private readonly Queue<ServerLogEntry> _logs = new();
    private readonly Decoder _logDecoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _logRemainder = new();
    private Process? _process;
    private bool _everReady;
    private bool _initialized;
    private bool _stopRequested;
    private bool _shutdownSent;
    private long _logOffset;
    private long _sequence;
    private DateTimeOffset? _lastCpuAt;
    private TimeSpan _lastCpu;
    private ServerRuntimeSnapshot _snapshot;

    public ServerSupervisor(ServerProfile profile, string hostDllPath, string runtimeDirectory, ControlConnection? control = null)
    {
        Profile = profile;
        _hostDllPath = Path.GetFullPath(hostDllPath);
        _runtimeDirectory = Path.GetFullPath(runtimeDirectory);
        _metadataPath = Path.Combine(_runtimeDirectory, "process.json");
        LogPath = Path.Combine(_runtimeDirectory, "server.log");
        _control = control ?? new ControlConnection(TimeSpan.FromSeconds(5));
        _snapshot = new ServerRuntimeSnapshot { Id = profile.Id };
    }

    public ServerProfile Profile { get; }
    public string LogPath { get; }
    public string LogStreamId { get; } = Guid.NewGuid().ToString("N");
    public ServerRuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public event Action<ServerLogEntry>? LogReceived;

    public IReadOnlyList<ServerLogEntry> GetLogs(long after = 0, int limit = 500)
    {
        lock (_logGate) return _logs.Where(entry => entry.Sequence > after).Take(Math.Clamp(limit, 1, LogCapacity)).ToArray();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await InitializeCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            StartCore(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            StartCore(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            await TailLogAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<ControlResponse> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        if (command is not ("status" or "pause" or "resume" or "save"))
            throw new ArgumentException("Use the lifecycle methods for shutdown or restart.", nameof(command));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLiveOwnedProcess();
            if (!_everReady) throw new InvalidOperationException("The server is still starting.");
            var response = await _control.ExecuteAsync(ControlHost, Profile.ControlPort, KeyPath, command, cancellationToken).ConfigureAwait(false);
            if (command == "status") ApplyStatus(response.Message);
            else AddLog("info", $"Manager: {command} accepted by the server.");
            return response;
        }
        finally { _gate.Release(); }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        Directory.CreateDirectory(_runtimeDirectory);
        _initialized = true;
        if (!File.Exists(_metadataPath)) return;
        Process? candidate = null;
        try
        {
            var info = new FileInfo(_metadataPath);
            if (info.Length > 16384) throw new IOException("Process metadata is too large.");
            var metadata = JsonSerializer.Deserialize<OwnedProcess>(await File.ReadAllTextAsync(_metadataPath, cancellationToken).ConfigureAwait(false))
                ?? throw new IOException("Process metadata is empty.");
            if (metadata.ProfileId != Profile.Id || !SamePath(metadata.HostDllPath, _hostDllPath) || !SamePath(metadata.DataDirectory, Profile.DataDirectory))
                throw new IOException("Process metadata does not match this server profile.");
            candidate = Process.GetProcessById(metadata.ProcessId);
            if (candidate.HasExited || candidate.StartTime.ToUniversalTime().Ticks != metadata.StartTimeUtcTicks)
                throw new IOException("The recorded server process has exited or its process ID was reused.");
            string processName = candidate.ProcessName;
            string executable = candidate.MainModule?.FileName ?? throw new IOException("Cannot identify the recorded server executable.");
            bool isDotnet = processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
            bool isAppHost = SamePath(executable, AppHostPath);
            if ((!isDotnet && !isAppHost) || metadata.ExecutablePath is not null && !SamePath(executable, metadata.ExecutablePath))
                throw new IOException("The recorded process is not the dedicated host runtime.");
            _process = candidate;
            candidate = null;
            _everReady = metadata.WasReady;
            _stopRequested = metadata.StopRequested;
            _snapshot = new ServerRuntimeSnapshot
            {
                Id = Profile.Id, State = _stopRequested ? ServerState.Stopping : ServerState.Starting,
                IsOwned = true, ProcessId = _process.Id,
                StartedUtc = new DateTimeOffset(new DateTime(metadata.StartTimeUtcTicks, DateTimeKind.Utc))
            };
            // Reopening a manager should show recent output without reading an unbounded historical log.
            if (File.Exists(LogPath)) _logOffset = Math.Max(0, new FileInfo(LogPath).Length - 256 * 1024);
            AddLog("info", $"Manager reattached to server process {_process.Id}.");
            await TailLogAsync(cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { _initialized = false; throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            AddLog("warning", $"Manager did not attach to the recorded process: {ex.Message}");
            if (_process is null)
            {
                _snapshot = new ServerRuntimeSnapshot { Id = Profile.Id, State = ServerState.Stopped, LastError = ex.Message };
                TryRemoveMetadata();
            }
            else
            {
                // Once identity has been verified, a log or metadata I/O failure must not
                // make a live process appear stopped and permit replacement of its save.
                _snapshot = _snapshot with { State = ServerState.Unresponsive, LastError = ex.Message };
            }
        }
        finally { candidate?.Dispose(); }
    }

    private void StartCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_process is not null)
        {
            if (!_process.HasExited) throw new InvalidOperationException("This server already has a live process.");
            RecordExit();
        }
        if (!File.Exists(_hostDllPath)) throw new FileNotFoundException("Build the dedicated server before starting it.", _hostDllPath);
        if (!Directory.Exists(Profile.GameDirectory)) throw new DirectoryNotFoundException("The configured Captain of Industry installation was not found.");
        if (!IPAddress.TryParse(Profile.BindAddress, out var bindAddress)) throw new ArgumentException("Server listen address must be an IP address.");
        if (Profile.ControlPort is < 1 or > 65535 || Profile.GamePort is < 1 or > 65535 || Profile.ControlPort == Profile.GamePort)
            throw new ArgumentException("Control and player ports must be valid and different.");
        EnsurePortFree(bindAddress, Profile.ControlPort);
        EnsurePortFree(bindAddress, Profile.GamePort);
        Directory.CreateDirectory(Profile.DataDirectory);
        Directory.CreateDirectory(_runtimeDirectory);
        _everReady = false;
        _stopRequested = false;
        _shutdownSent = false;
        _lastCpuAt = null;
        // Only inspect output from this new process for readiness.
        _logOffset = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
        _logRemainder.Clear(); _logDecoder.Reset();
        bool useAppHost = File.Exists(AppHostPath);
        var start = new ProcessStartInfo
        {
            FileName = useAppHost ? AppHostPath : "dotnet", WorkingDirectory = Path.GetDirectoryName(_hostDllPath)!,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = false, RedirectStandardError = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (!useAppHost) start.ArgumentList.Add(_hostDllPath);
        foreach (string argument in new[]
        {
            "--serve", "--game-dir", Path.GetFullPath(Profile.GameDirectory),
            "--data-dir", Path.GetFullPath(Profile.DataDirectory), "--map", Profile.Map,
            "--listen", Profile.BindAddress, "--port", Profile.ControlPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--game-port", Profile.GamePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--autosave-seconds", Profile.AutosaveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--log-file", LogPath
        }) start.ArgumentList.Add(argument);
        if (Profile.StartingFactory) start.ArgumentList.Add("--starting-factory");
        Process process = Process.Start(start) ?? throw new InvalidOperationException("The server process could not be started.");
        _process = process;
        _snapshot = new ServerRuntimeSnapshot
        {
            Id = Profile.Id, State = ServerState.Starting, IsOwned = true,
            ProcessId = process.Id, StartedUtc = process.StartTime.ToUniversalTime()
        };
        try { PersistMetadata(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retain the live process handle instead of risking an unsaved forced stop.
            _snapshot = _snapshot with { LastError = "The server started, but manager ownership could not be saved. Stop it before closing this manager." };
            AddLog("error", _snapshot.LastError);
            throw;
        }
        AddLog("info", $"Manager started server process {process.Id}; waiting for authenticated readiness.");
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        if (_process is null) return;
        if (_process.HasExited) { RecordExit(); return; }
        EnsureLiveOwnedProcess();
        await TailLogAsync(cancellationToken).ConfigureAwait(false);
        _stopRequested = true;
        _snapshot = _snapshot with { State = ServerState.Stopping, LastError = null };
        PersistMetadata();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            while (!_shutdownSent)
            {
                bool wasReadyWhenRequested = _everReady;
                try { await RequestShutdownAsync(deadline.Token).ConfigureAwait(false); }
                catch (Exception ex) when (IsControlFailure(ex))
                {
                    await TailLogAsync(deadline.Token).ConfigureAwait(false);
                    if (_process.HasExited) { RecordExit(); return; }
                    if (wasReadyWhenRequested)
                    {
                        _snapshot = _snapshot with { State = ServerState.Unresponsive, LastError = "Graceful shutdown could not reach the server. Its process is still running; no forced stop was performed." };
                        AddLog("error", _snapshot.LastError);
                        throw new IOException(_snapshot.LastError, ex);
                    }
                    // Wait for bootstrap to finish, then save and stop through the authenticated endpoint.
                    // A check-then-kill bootstrap path would race the start of the world simulation.
                    await Task.Delay(200, deadline.Token).ConfigureAwait(false);
                }
            }
            await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _snapshot = _snapshot with { State = ServerState.Stopping, LastError = "The server is still initializing, saving, or shutting down after 60 seconds. The stop remains pending; its process has not been force-stopped." };
            AddLog("warning", _snapshot.LastError);
            throw new TimeoutException(_snapshot.LastError);
        }
        await TailLogAsync(CancellationToken.None).ConfigureAwait(false);
        RecordExit();
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (_process is null) return;
        if (_process.HasExited) { RecordExit(); return; }
        UpdateMetrics();
        try
        {
            var response = await _control.ExecuteAsync(ControlHost, Profile.ControlPort, KeyPath, "status", cancellationToken).ConfigureAwait(false);
            ApplyStatus(response.Message);
            if (!_everReady)
            {
                _everReady = true;
                PersistMetadata();
                AddLog("info", "Server is ready; authenticated control connection established.");
            }
            if (_stopRequested && !_shutdownSent) await RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsControlFailure(ex))
        {
            if (_process.HasExited) { RecordExit(); return; }
            var age = DateTimeOffset.UtcNow - (_snapshot.StartedUtc ?? DateTimeOffset.UtcNow);
            ServerState state = _stopRequested ? ServerState.Stopping : _everReady || age > TimeSpan.FromMinutes(5) ? ServerState.Unresponsive : ServerState.Starting;
            _snapshot = _snapshot with { State = state, LastError = state == ServerState.Starting ? null : ex.Message };
        }
    }

    private void ApplyStatus(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("step", out var step) || !step.TryGetInt32(out int currentStep) || !root.TryGetProperty("paused", out var paused) || paused.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new IOException("Server returned an invalid status response.");
        _snapshot = _snapshot with
        {
            State = _stopRequested ? ServerState.Stopping : paused.GetBoolean() ? ServerState.Paused : ServerState.Running,
            LastStatusUtc = DateTimeOffset.UtcNow, LastError = null, Step = currentStep,
            Players = root.TryGetProperty("players", out var players) && players.TryGetInt32(out var playerCount) ? playerCount : null,
            Joining = root.TryGetProperty("joining", out var joining) && joining.ValueKind is JsonValueKind.True or JsonValueKind.False ? joining.GetBoolean() : null,
            UptimeSeconds = root.TryGetProperty("uptimeSeconds", out var uptime) && uptime.TryGetInt32(out var seconds) ? seconds : null,
            World = root.TryGetProperty("world", out var world) ? world.Clone() : null
        };
    }

    private void UpdateMetrics()
    {
        try
        {
            _process!.Refresh();
            var now = DateTimeOffset.UtcNow;
            var cpu = _process.TotalProcessorTime;
            double? percent = _lastCpuAt is { } previous && now > previous
                ? Math.Clamp((cpu - _lastCpu).TotalSeconds / (now - previous).TotalSeconds / Environment.ProcessorCount * 100, 0, 100) : null;
            _snapshot = _snapshot with { MemoryBytes = _process.WorkingSet64, CpuPercent = percent };
            _lastCpuAt = now; _lastCpu = cpu;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private async Task TailLogAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LogPath)) return;
        try
        {
            await using var file = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16384, FileOptions.Asynchronous);
            if (file.Length < _logOffset) { _logOffset = 0; _logDecoder.Reset(); _logRemainder.Clear(); }
            file.Position = _logOffset;
            byte[] bytes = new byte[16384];
            char[] chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            int consumed = 0;
            while (consumed < 256 * 1024)
            {
                int read = await file.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                consumed += read; _logOffset += read;
                int count = _logDecoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                for (int index = 0; index < count; index++)
                {
                    char value = chars[index];
                    if (value == '\n')
                    {
                        string line = _logRemainder.ToString().TrimEnd('\r'); _logRemainder.Clear();
                        if (line.Contains("[server] Simulation continues with no players.", StringComparison.Ordinal)) _everReady = true;
                        if (line.Length != 0) AddLog(line.Contains("exception", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase) ? "error" : line.Contains("warning", StringComparison.OrdinalIgnoreCase) ? "warning" : "info", line);
                    }
                    else if (_logRemainder.Length < 8192) _logRemainder.Append(value);
                }
            }
        }
        catch (IOException) { }
    }

    private void AddLog(string level, string text)
    {
        ServerLogEntry entry;
        lock (_logGate)
        {
            entry = new ServerLogEntry(++_sequence, DateTimeOffset.UtcNow, level, text.Length > 8192 ? text[..8192] : text);
            _logs.Enqueue(entry);
            while (_logs.Count > LogCapacity) _logs.Dequeue();
        }
        try { LogReceived?.Invoke(entry); }
        catch { /* An observer cannot break supervision. */ }
    }

    private void RecordExit()
    {
        if (_process is null || !_process.HasExited) return;
        int? code = null;
        try { code = _process.ExitCode; }
        catch (InvalidOperationException) { }
        bool expected = code == 0;
        _snapshot = _snapshot with
        {
            State = expected ? ServerState.Stopped : ServerState.Faulted,
            IsOwned = false, ProcessId = null, ExitCode = code, MemoryBytes = null, CpuPercent = null,
            LastError = expected ? null : $"Server exited unexpectedly with code {code?.ToString() ?? "unknown"}. Check the server log."
        };
        AddLog(expected ? "info" : "error", $"Server process exited with code {code?.ToString() ?? "unknown"}.");
        _process.Dispose(); _process = null;
        TryRemoveMetadata();
    }

    private void EnsureLiveOwnedProcess()
    {
        if (_process is null || !_snapshot.IsOwned || _process.HasExited)
            throw new InvalidOperationException("There is no live server process owned by this profile.");
    }

    private void PersistMetadata()
    {
        if (_process is null) return;
        var data = new OwnedProcess(Profile.Id, _process.Id, _process.StartTime.ToUniversalTime().Ticks, _hostDllPath, Path.GetFullPath(Profile.DataDirectory), _everReady, _stopRequested, _process.MainModule?.FileName);
        string temporary = _metadataPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data));
        File.Move(temporary, _metadataPath, overwrite: true);
    }

    private void TryRemoveMetadata()
    {
        try { File.Delete(_metadataPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AddLog("warning", "Could not remove stale manager process metadata."); }
    }

    private async Task RequestShutdownAsync(CancellationToken cancellationToken)
    {
        await _control.ExecuteAsync(ControlHost, Profile.ControlPort, KeyPath, "shutdown", cancellationToken).ConfigureAwait(false);
        _everReady = true; _shutdownSent = true;
        PersistMetadata();
        AddLog("info", "Manager requested a final save and graceful shutdown.");
    }

    private static void EnsurePortFree(IPAddress address, int port)
    {
        var listener = new TcpListener(address, port) { ExclusiveAddressUse = true };
        try { listener.Start(); }
        catch (SocketException ex) { throw new InvalidOperationException($"Port {port} is already in use or cannot be opened. No server was started.", ex); }
        finally { listener.Stop(); }
    }

    private string KeyPath => Path.Combine(Profile.DataDirectory, "control.key");
    private string AppHostPath => Path.ChangeExtension(_hostDllPath, OperatingSystem.IsWindows() ? ".exe" : null);
    private string ControlHost => Profile.BindAddress is "0.0.0.0" ? "127.0.0.1" : Profile.BindAddress is "::" ? "::1" : Profile.BindAddress;
    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool IsControlFailure(Exception exception) => exception is IOException or SocketException or TimeoutException or UnauthorizedAccessException or JsonException;
    private sealed record OwnedProcess(string ProfileId, int ProcessId, long StartTimeUtcTicks, string HostDllPath, string DataDirectory, bool WasReady, bool StopRequested, string? ExecutablePath = null);

    /// <summary>Detaches the manager while leaving its server and durable ownership metadata intact.</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_process is not null)
            {
                if (_process.HasExited) RecordExit();
                else { PersistMetadata(); _process.Dispose(); _process = null; }
            }
        }
        finally { _gate.Release(); }
    }
}
