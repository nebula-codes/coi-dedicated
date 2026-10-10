namespace CoiDedicated;

internal sealed record HostOptions(string GameDirectory, string DataDirectory, string Map, string? Load, int Steps, bool Inspect, bool Help)
{
    public bool Serve { get; init; }
    public bool StartingFactory { get; init; }
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 28550;
    public int GamePort { get; init; } = 28551;
    public string? JoinSecretFile { get; init; }
    public bool Diagnostics { get; init; }
    public string? LogFile { get; init; }
    public int RunSeconds { get; init; }
    public int AutosaveSeconds { get; init; } = 60;
    public string? SecretFile { get; init; }
    public static HostOptions Parse(string[] args)
    {
        string game = Environment.GetEnvironmentVariable("COI_GAME_DIR") ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry";
        string data = Path.GetFullPath("server-data");
        string map = "Sandbox";
        string? load = null;
        int steps = 100;
        bool inspect = false, help = false;
        bool serve = false, startingFactory = false;
        bool diagnostics = false;
        string? logFile = null;
        string bind = "127.0.0.1";
        string? secretFile = null;
        string? joinSecretFile = null;
        int gamePort = 28551;
        int port = 28550, runSeconds = 0, autosaveSeconds = 60;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {args[i-1]}");
            switch (args[i])
            {
                case "--game-dir": game = Value(); break;
                case "--data-dir": data = Value(); break;
                case "--map": map = Value(); break;
                case "--load": load = Path.GetFullPath(Value()); break;
                case "--steps": steps = int.Parse(Value()); break;
                case "--inspect": inspect = true; break;
                case "--serve": serve = true; break;
                case "--diagnostics": diagnostics = true; break;
                case "--log-file": logFile = Path.GetFullPath(Value()); break;
                case "--starting-factory": startingFactory = true; break;
                case "--listen": bind = Value(); break;
                case "--port": port = int.Parse(Value()); break;
                case "--game-port": gamePort = int.Parse(Value()); break;
                case "--join-secret-file": joinSecretFile = Path.GetFullPath(Value()); break;
                case "--run-seconds": runSeconds = int.Parse(Value()); break;
                case "--autosave-seconds": autosaveSeconds = int.Parse(Value()); break;
                case "--secret-file": secretFile = Path.GetFullPath(Value()); break;
                case "--help" or "-h": help = true; break;
                default: throw new ArgumentException($"Unknown option {args[i]}");
            }
        }
        if (steps < 0 || steps > 10_000_000) throw new ArgumentOutOfRangeException(nameof(steps));
        if (port < 1 || port > 65535 || runSeconds < 0 || autosaveSeconds < 10) throw new ArgumentException("Invalid server port, duration or autosave interval.");
        if (gamePort < 1 || gamePort > 65535 || gamePort == port) throw new ArgumentException("Game port must be valid and differ from the control port.");
        if (!System.Net.IPAddress.TryParse(bind, out _)) throw new ArgumentException("--listen must be an IP address.");
        if (serve && load is null && File.Exists(Path.Combine(data, "island.save"))) load = Path.GetFullPath(Path.Combine(data, "island.save"));
        return new(Path.GetFullPath(game), Path.GetFullPath(data), map, load, steps, inspect, help)
        {
            Serve = serve, StartingFactory = startingFactory, BindAddress = bind, Port = port,
            RunSeconds = runSeconds, AutosaveSeconds = autosaveSeconds, SecretFile = secretFile,
            GamePort = gamePort, JoinSecretFile = joinSecretFile, Diagnostics = diagnostics, LogFile = logFile
        };
    }

    public static void PrintHelp() => Console.WriteLine("""
        CoI dedicated simulation prototype
          --game-dir PATH       Installed game files (or COI_GAME_DIR)
          --data-dir PATH       Isolated server data (default server-data)
          --map NAME            Shipped map (default Sandbox)
          --starting-factory    Populate a new island
          --load PATH           Load an existing save instead of a new island
          --steps COUNT         Updates before saving and exiting (without --serve)
          --serve               Run continuously; resume data-dir/island.save if present
          --listen IP           Bind both endpoints (default 127.0.0.1)
          --port NUMBER         Admin endpoint port (default 28550)
          --game-port NUMBER    Player endpoint port (default 28551)
          --secret-file PATH    Admin key file (generated if missing)
          --join-secret-file PATH  Player key file (generated if missing)
          --autosave-seconds N  Save interval (default 60, minimum 10)
          --run-seconds N       Stop after N seconds (0 = until stopped)
          --diagnostics         Write a raw state diagnostic on service shutdown
          --log-file PATH       Append logs to a file owned by the server process
          --inspect             Verify assembly loading and fixed-point arithmetic
        """);
}
