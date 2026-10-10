namespace CoiDedicated.Manager;

internal sealed record ManagerOptions(string RepositoryRoot, string DataRoot, string HostDll, int Port, bool OpenBrowser)
{
    public static ManagerOptions Parse(string[] args)
    {
        string? repository = null, data = null, host = null;
        int port = 28560;
        bool open = false;
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
                default: throw new ArgumentException($"Unknown manager option: {args[i]}");
            }
        }
        repository ??= FindRepository();
        if (port is < 1024 or > 65535) throw new ArgumentException("Manager port must be between 1024 and 65535.");
        string packagedHost = Path.Combine(repository, "bin", "server", "CoiDedicated.dll");
        bool packaged = File.Exists(packagedHost);
        return new(repository, data ?? (packaged ? Path.Combine(repository, "data", "default") : Path.Combine(repository, "manager-data")),
            host ?? (packaged ? packagedHost : Path.Combine(repository, "artifacts", "manager-host", "CoiDedicated.dll")), port, open);
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
