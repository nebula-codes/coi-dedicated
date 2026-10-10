using System.Net;
using System.Text.Json;

namespace CoiDedicated.Manager.Core;

/// <summary>Local profile configuration. Server data paths are owned by the manager.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _configurationPath;
    private List<ServerProfile> _profiles = [];
    private bool _initialized;

    public string RootDirectory { get; }

    public ProfileStore(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        _configurationPath = Path.Combine(RootDirectory, "profiles.json");
    }

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            Directory.CreateDirectory(RootDirectory);
            if (File.Exists(_configurationPath))
            {
                var stored = JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(_configurationPath), JsonOptions)
                    ?? throw new InvalidDataException("Profile configuration is empty or invalid.");
                if (stored.Count > 100) throw new InvalidDataException("Profile configuration contains too many servers.");
                foreach (var profile in stored)
                {
                    ValidateId(profile.Id);
                    if (_profiles.Any(p => p.Id == profile.Id)) throw new InvalidDataException("Profile configuration contains a duplicate identifier.");
                    // Do not make a removed Steam drive prevent access to every other profile.
                    var normalized = Normalize(ToInput(profile), profile.Id, requireGameFiles: false);
                    _profiles.Add(FromInput(profile.Id, normalized, profile.CreatedUtc));
                }
            }
            else
            {
                // A portable installation must reach Settings even before the game path
                // is configured. Starting a server still validates the installed files.
                string gameDirectory = GetDefaultGameDirectory();
                var input = Normalize(new("New Haven", gameDirectory, "New Haven", "127.0.0.1", 28550, 28551, 60, true), null, false);
                var id = Guid.NewGuid().ToString("N");
                _profiles.Add(FromInput(id, input, DateTimeOffset.UtcNow));
                Persist(_profiles);
            }
            _initialized = true;
        }
    }

    public IReadOnlyList<ServerProfile> List()
    {
        lock (_gate) { EnsureInitialized(); return _profiles.ToArray(); }
    }

    public ServerProfile Get(string id)
    {
        ValidateId(id);
        lock (_gate)
        {
            EnsureInitialized();
            return _profiles.SingleOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException("Server profile was not found.");
        }
    }

    public ServerProfile Create(ProfileInput input)
    {
        lock (_gate)
        {
            EnsureInitialized();
            if (_profiles.Count >= 100) throw new ArgumentException("The manager supports up to 100 server profiles.");
            var normalized = Normalize(input, null, true);
            var profile = FromInput(Guid.NewGuid().ToString("N"), normalized, DateTimeOffset.UtcNow);
            var updated = _profiles.Append(profile).ToList();
            Persist(updated);
            _profiles = updated;
            return profile;
        }
    }

    public ServerProfile Update(string id, ProfileInput input)
    {
        lock (_gate)
        {
            var existing = Get(id);
            var normalized = Normalize(input, id, true);
            var profile = FromInput(id, normalized, existing.CreatedUtc);
            var updated = _profiles.Select(p => p.Id == id ? profile : p).ToList();
            Persist(updated);
            _profiles = updated;
            return profile;
        }
    }

    public string GetDataDirectory(string id) => Get(id).DataDirectory;

    public static string GetDefaultGameDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable("COI_GAME_DIR");
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFiles)) programFiles = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(programFiles, "Steam", "steamapps", "common", "Captain of Industry"));
    }

    public IReadOnlyList<string> GetMaps(string gameDirectory)
    {
        string normalized = NormalizeGameDirectory(gameDirectory, true);
        return Directory.GetFiles(Path.Combine(normalized, "Maps"), "*.map", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void ValidateForStart(ServerProfile profile)
    {
        lock (_gate) { EnsureInitialized(); Normalize(ToInput(profile), profile.Id, true); }
    }

    internal static void ValidateId(string id)
    {
        if (id is null || id.Length != 32 || !Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Invalid identifier.");
    }

    private ProfileInput Normalize(ProfileInput input, string? currentId, bool requireGameFiles)
    {
        ArgumentNullException.ThrowIfNull(input);
        string name = input.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl)) throw new ArgumentException("Server name must contain 1–64 printable characters.");
        string game = NormalizeGameDirectory(input.GameDirectory, requireGameFiles);
        string map = input.Map?.Trim() ?? "";
        if (map.Length is < 1 or > 128 || map.Any(char.IsControl) || map.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || map.Contains('/') || map.Contains('\\') || map is "." or "..")
            throw new ArgumentException("Select a shipped map name without a path or extension.");
        if (requireGameFiles && !File.Exists(Path.Combine(game, "Maps", map + ".map")))
            throw new ArgumentException("The selected map is not installed in the game Maps folder.");
        if (!IPAddress.TryParse(input.BindAddress?.Trim(), out var address)) throw new ArgumentException("Listen address must be a literal IPv4 or IPv6 address.");
        if (input.ControlPort is < 1 or > 65535 || input.GamePort is < 1 or > 65535 || input.ControlPort == input.GamePort)
            throw new ArgumentException("Control and player ports must be different numbers from 1 to 65535.");
        if (_profiles.Where(p => p.Id != currentId).Any(p => p.ControlPort == input.ControlPort || p.ControlPort == input.GamePort
            || p.GamePort == input.ControlPort || p.GamePort == input.GamePort))
            throw new ArgumentException("These ports are already assigned to another server profile.");
        if (input.AutosaveSeconds is < 10 or > 3600) throw new ArgumentException("Autosave interval must be between 10 and 3600 seconds.");
        return input with { Name = name, GameDirectory = game, Map = map, BindAddress = address.ToString() };
    }

    private static string NormalizeGameDirectory(string directory, bool requireGameFiles)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Game directory must be an absolute path.");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim()));
        if (requireGameFiles)
        {
            string managed = Path.Combine(fullPath, "Captain of Industry_Data", "Managed");
            foreach (var name in new[] { "Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll" })
                if (!File.Exists(Path.Combine(managed, name))) throw new ArgumentException($"Game directory is missing {name}.");
            if (!Directory.Exists(Path.Combine(fullPath, "Maps"))) throw new ArgumentException("Game directory is missing its Maps folder.");
        }
        return fullPath;
    }

    private ServerProfile FromInput(string id, ProfileInput input, DateTimeOffset created) => new(id, input.Name,
        input.GameDirectory, Path.Combine(RootDirectory, "servers", id), input.Map, input.BindAddress,
        input.ControlPort, input.GamePort, input.AutosaveSeconds, input.StartingFactory, created);

    private static ProfileInput ToInput(ServerProfile profile) => new(profile.Name, profile.GameDirectory, profile.Map,
        profile.BindAddress, profile.ControlPort, profile.GamePort, profile.AutosaveSeconds, profile.StartingFactory);

    private void EnsureInitialized() { if (!_initialized) Initialize(); }

    private void Persist(List<ServerProfile> profiles)
    {
        string temporary = _configurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, profiles, JsonOptions);
                output.Flush(true);
            }
            if (File.Exists(_configurationPath)) File.Replace(temporary, _configurationPath, _configurationPath + ".previous");
            else File.Move(temporary, _configurationPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
