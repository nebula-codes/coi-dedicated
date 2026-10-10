using System.Globalization;
using System.Text;

namespace CoiDedicated.Peer;

internal sealed class PeerOptions
{
    public string Host { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 28551;
    public string Name { get; private set; } = "headless-peer";
    public string GameDirectory { get; private set; } = Environment.GetEnvironmentVariable("COI_GAME_DIR") ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry";
    public string DataDirectory { get; private set; } = Path.GetFullPath("peer-data");
    public string SecretFile { get; private set; } = "";
    public int Ticks { get; private set; } = 100;
    public int TimeoutSeconds { get; private set; } = 180;
    public int? PlaceMachineAt { get; private set; }
    public int? RejectedSpeedAt { get; private set; }
    public bool CorruptCheckpoint { get; private set; }
    public int? DivergeAt { get; private set; }
    public Dictionary<int, int> SpeedProposals { get; } = new();
    public bool Help { get; private set; }
    public Dictionary<int, bool> PauseProposals { get; } = new();

    public static PeerOptions Parse(string[] args)
    {
        var options = new PeerOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option is "--help" or "-h") { options.Help = true; continue; }
            if (option == "--corrupt-checkpoint") { options.CorruptCheckpoint = true; continue; }
            if (option is not ("--host" or "--port" or "--name" or "--game-dir" or "--data-dir" or "--secret-file" or "--ticks" or "--timeout-seconds" or "--pause-at" or "--resume-at" or "--place-machine-at" or "--reject-speed-at" or "--speed-at" or "--diverge-at"))
                throw new ArgumentException($"Unknown peer option: {option}");
            if (++index >= args.Length) throw new ArgumentException($"Missing value for {option}.");
            string value = args[index];
            switch (option)
            {
                case "--host": options.Host = value; break;
                case "--port": options.Port = ParseInt(value, 1, 65535, option); break;
                case "--name": options.Name = value; break;
                case "--game-dir": options.GameDirectory = Path.GetFullPath(value); break;
                case "--data-dir": options.DataDirectory = Path.GetFullPath(value); break;
                case "--secret-file": options.SecretFile = Path.GetFullPath(value); break;
                case "--ticks": options.Ticks = ParseInt(value, 1, 100000, option); break;
                case "--timeout-seconds": options.TimeoutSeconds = ParseInt(value, 1, 3600, option); break;
                case "--place-machine-at": options.PlaceMachineAt = ParseInt(value, 1, 100000, option); break;
                case "--reject-speed-at": options.RejectedSpeedAt = ParseInt(value, 1, 100000, option); break;
                case "--diverge-at": options.DivergeAt = ParseInt(value, 1, 100000, option); break;
                case "--speed-at":
                    string[] parts = value.Split(':');
                    if (parts.Length != 2 || !options.SpeedProposals.TryAdd(ParseInt(parts[0], 1, 100000, option), ParseInt(parts[1], 1, 4, option)))
                        throw new ArgumentException("--speed-at requires unique TICK:SPEED (1..4).");
                    break;
                case "--pause-at":
                case "--resume-at":
                    int tick = ParseInt(value, 1, 100000, option);
                    if (!options.PauseProposals.TryAdd(tick, option == "--pause-at"))
                        throw new ArgumentException($"Two proposals configured at local tick {tick}.");
                    break;
            }
        }
        if (options.Help) return options;
        if (options.SecretFile.Length == 0) throw new ArgumentException("--secret-file must point to the host join.key.");
        if (string.IsNullOrWhiteSpace(options.Host)) throw new ArgumentException("Host cannot be empty.");
        if (string.IsNullOrWhiteSpace(options.Name) || new UTF8Encoding(false, true).GetByteCount(options.Name) > 64)
            throw new ArgumentException("Peer name must contain 1..64 UTF-8 bytes.");
        foreach (int tick in options.PauseProposals.Keys)
            if (tick >= options.Ticks) throw new ArgumentException("Proposals must be earlier than the final requested tick, so their broadcasts can be observed.");
        if (options.PlaceMachineAt >= options.Ticks) throw new ArgumentException("Machine placement must occur before the final requested tick.");
        if (options.RejectedSpeedAt >= options.Ticks) throw new ArgumentException("Speed rejection fixture must occur before the final requested tick.");
        options.GameDirectory = Path.GetFullPath(options.GameDirectory);
        return options;
    }

    private static int ParseInt(string value, int min, int max, string option)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
            throw new ArgumentException($"{option} must be an integer from {min} to {max}.");
        return parsed;
    }

    public static void PrintHelp() => Console.WriteLine("Headless CoI gameplay peer integration harness\n  --secret-file PATH  Host join.key (required)\n  --host HOST         Default 127.0.0.1\n  --port N            Default 28551\n  --name NAME         Default headless-peer\n  --game-dir PATH     Installed game files\n  --data-dir PATH     Isolated downloads/reports (default peer-data)\n  --ticks N           Receive and verify N ticks (default 100)\n  --pause-at N        Propose pause after local tick N; repeatable\n  --resume-at N       Propose resume after local tick N; repeatable\n  --place-machine-at N  Find and propose one ordinary machine placement\n  --reject-speed-at N  Verify rejection of speed 5 (multiplayer permits 1..4)\n  --speed-at N:SPEED  Propose speed 1..4 after local tick N; repeatable\n  --diverge-at N      TEST ONLY: mutate one local entity; require resync\n  --corrupt-checkpoint TEST ONLY: corrupt digest; require resync\n  --timeout-seconds N Overall deadline including load (default 180)");
}
