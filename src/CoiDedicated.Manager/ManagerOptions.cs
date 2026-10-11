using System.Net;

namespace CoiDedicated.Manager;

internal sealed record ManagerOptions(string RepositoryRoot, string DataRoot, string HostDll, int Port, bool OpenBrowser,
    bool ContainerMode = false, string ListenAddress = "127.0.0.1", string? GameDirectory = null,
    string? ServerDataDirectory = null, string Map = "New Haven", int AutosaveSeconds = 60,
    bool StartingFactory = true, bool AutoStart = true, string ServerName = "My island", string[]? AllowedHosts = null, int PublicGamePort = 28551)
{
    public bool RequiresAuthentication => ContainerMode || !IPAddress.IsLoopback(IPAddress.Parse(ListenAddress));

    public static ManagerOptions Parse(string[] args)
    {
        string? repository = null, data = null, host = null, listen = null, game = null, serverData = null;
        string map = "New Haven", name = "My island";
        int port = 28560, autosave = 60, publicGamePort = 28551;
        bool open = false, container = false, factory = true, autoStart = true;
        List<string> allowedHosts = [];
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
            switch (args[i])
            {
                case "--repository": repository = Path.GetFullPath(Value()); break;
                case "--data-dir": data = Path.GetFullPath(Value()); break;
                case "--host-dll": host = Path.GetFullPath(Value()); break;
                case "--port": port = int.Parse(Value()); break;
                case "--open": open = true; break;
                case "--container": container = true; break;
                case "--listen": listen = Value(); break;
                case "--game-dir": game = Path.GetFullPath(Value()); break;
                case "--server-data-dir": serverData = Path.GetFullPath(Value()); break;
                case "--map": map = Value(); break;
                case "--name": name = Value(); break;
                case "--autosave-seconds": autosave = int.Parse(Value()); break;
                case "--starting-factory": factory = true; break;
                case "--no-starting-factory": factory = false; break;
                case "--no-autostart": autoStart = false; break;
                case "--allowed-host": allowedHosts.Add(Value().Trim()); break;
                case "--public-game-port": publicGamePort = int.Parse(Value()); break;
                default: throw new ArgumentException($"Unknown manager option: {args[i]}");
            }
        }
        repository ??= container ? "/app" : FindRepository();
        listen ??= container ? "0.0.0.0" : "127.0.0.1";
        if (!IPAddress.TryParse(listen, out var address)) throw new ArgumentException("Manager listen address must be a literal IP address.");
        if (port is < 1024 or > 65535) throw new ArgumentException("Manager port must be between 1024 and 65535.");
        if (autosave is < 10 or > 3600) throw new ArgumentException("Autosave interval must be between 10 and 3600 seconds.");
        if (publicGamePort is < 1 or > 65535) throw new ArgumentException("Published game port must be between 1 and 65535.");
        if (container && (game is null || serverData is null)) throw new ArgumentException("Container mode requires --game-dir and --server-data-dir.");
        if (container && port is 28550 or 28551) throw new ArgumentException("The manager port must differ from the container game and control ports.");
        foreach (string allowed in allowedHosts)
            if (allowed.Length == 0 || allowed.Contains('*') || Uri.CheckHostName(allowed) is not UriHostNameType.Dns)
                throw new ArgumentException("Each --allowed-host must be an exact DNS hostname without a scheme, port, or wildcard.");
        string packagedHost = Path.Combine(repository, "bin", "server", "CoiDedicated.dll");
        bool packaged = File.Exists(packagedHost);
        return new(repository, data ?? (container ? Path.Combine(serverData!, ".manager") : packaged ? Path.Combine(repository, "data", "default") : Path.Combine(repository, "manager-data")),
            host ?? (container ? Path.Combine(repository, "server", "CoiDedicated.dll") : packaged ? packagedHost : Path.Combine(repository, "artifacts", "manager-host", "CoiDedicated.dll")),
            port, open, container, address.ToString(), game, serverData, map, autosave, factory, autoStart, name, allowedHosts.ToArray(), publicGamePort);
    }

    private static string FindRepository()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "CoiDedicated.slnx"))
                    || File.Exists(Path.Combine(directory.FullName, "bin", "server", "CoiDedicated.dll"))
                        && File.Exists(Path.Combine(directory.FullName, "bin", "manager", "CoiDedicated.Manager.dll"))) return directory.FullName;
        throw new DirectoryNotFoundException("Pass --repository with the extracted server package or CoI dedicated project directory.");
    }
}
