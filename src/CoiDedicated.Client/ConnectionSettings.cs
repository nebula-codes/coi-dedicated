using System;
using System.IO;
using BepInEx.Configuration;

namespace CoiDedicated.Client;

/// <summary>Persists connection locations, never the contents of a player key.</summary>
internal sealed class ConnectionSettings
{
    private readonly ConfigFile config;
    private readonly ConfigEntry<string> host;
    private readonly ConfigEntry<int> port;
    private readonly ConfigEntry<string> keyFile;
    private readonly ConfigEntry<string> name;

    internal ConnectionSettings(ConfigFile config)
    {
        this.config = config;
        host = config.Bind("Connection", "Host", "127.0.0.1", "Dedicated gameplay server host.");
        port = config.Bind("Connection", "Port", 28551, "Dedicated gameplay server port.");
        keyFile = config.Bind("Connection", "JoinKeyFile", "", "Path to join.key; key contents are never saved in this config.");
        name = config.Bind("Connection", "ClientName", "", "Player name: ASCII letters, digits, underscore or hyphen. Empty chooses a random ID.");
    }

    internal string Host => host.Value;
    internal int Port => port.Value;
    internal string KeyFile => keyFile.Value;
    internal string Name => name.Value;

    internal static string NormalizeKeyPath(string value)
    {
        string path = value.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
            path = path.Substring(1, path.Length - 2);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose your join.key file.");
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    internal void Save(string server, int number, string path, string player)
    {
        server = server.Trim();
        player = player.Trim();
        if (server.Length == 0 || server.Length > 255) throw new ArgumentException("Enter a server hostname.");
        if (number < 1 || number > 65535) throw new ArgumentException("Port must be from 1 to 65535.");
        if (player.Length > 64 || player == "server") throw new ArgumentException("Choose a player name of at most 64 characters other than 'server'.");
        foreach (char c in player)
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-')
                throw new ArgumentException("Player name must use letters, numbers, underscore or hyphen.");
        string absolutePath = NormalizeKeyPath(path);
        bool autoSave = config.SaveOnConfigSet;
        try
        {
            config.SaveOnConfigSet = false;
            host.Value = server;
            port.Value = number;
            keyFile.Value = absolutePath;
            name.Value = player;
            config.Save();
        }
        finally { config.SaveOnConfigSet = autoSave; }
    }
}
