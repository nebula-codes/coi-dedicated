using System.Text.Json;

namespace CoiDedicated.Manager.Core;

/// <summary>Remembers explicit start/stop intent separately from transient process state.</summary>
public sealed class RequestedRunStateStore
{
    private readonly string _path;
    private readonly bool _defaultRunning;
    private readonly object _gate = new();
    private Dictionary<string, bool>? _states;

    public RequestedRunStateStore(string rootDirectory, bool defaultRunning = true)
    {
        _path = Path.Combine(Path.GetFullPath(rootDirectory), "requested-run-state.json");
        _defaultRunning = defaultRunning;
    }

    public bool ShouldRun(string profileId)
    {
        ProfileStore.ValidateId(profileId);
        lock (_gate)
        {
            Load();
            return _states!.TryGetValue(profileId, out bool running) ? running : _defaultRunning;
        }
    }

    public void Set(string profileId, bool running)
    {
        ProfileStore.ValidateId(profileId);
        lock (_gate)
        {
            Load();
            var changed = new Dictionary<string, bool>(_states!) { [profileId] = running };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(output, changed);
                    output.Flush(true);
                }
                File.Move(temporary, _path, overwrite: true);
                _states = changed;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private void Load()
    {
        if (_states is not null) return;
        if (!File.Exists(_path)) { _states = []; return; }
        if (new FileInfo(_path).Length > 32768) throw new InvalidDataException("Requested server state file is too large.");
        var states = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Requested server state file is empty.");
        foreach (string id in states.Keys) ProfileStore.ValidateId(id);
        _states = states;
    }
}
