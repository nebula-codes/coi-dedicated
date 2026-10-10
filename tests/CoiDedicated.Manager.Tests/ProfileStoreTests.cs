using CoiDedicated.Manager.Core;

public static class ProfileStoreTests
{
    public static Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "coi-manager-profile-" + Guid.NewGuid().ToString("N"));
        string? originalGame = Environment.GetEnvironmentVariable("COI_GAME_DIR");
        try
        {
            string missing = Path.Combine(directory, "game-not-installed");
            Environment.SetEnvironmentVariable("COI_GAME_DIR", missing);
            var profiles = new ProfileStore(Path.Combine(directory, "data"));
            profiles.Initialize();
            var initial = profiles.List().Single();
            if (initial.GameDirectory != missing) throw new Exception("New profile did not preserve the configured game installation path.");
            bool startRejected = false;
            try { profiles.ValidateForStart(initial); }
            catch (ArgumentException) { startRejected = true; }
            if (!startRejected) throw new Exception("A profile without installed game files was allowed to start.");
            var reloaded = new ProfileStore(Path.Combine(directory, "data"));
            reloaded.Initialize();
            if (reloaded.List().Single().Id != initial.Id) throw new Exception("A profile without game files did not survive manager restart.");
            Environment.SetEnvironmentVariable("COI_GAME_DIR", "");
            var undiscovered = new ProfileStore(Path.Combine(directory, "undiscovered"));
            undiscovered.Initialize();
            if (!Path.IsPathFullyQualified(undiscovered.List().Single().GameDirectory)) throw new Exception("Default installation path is not absolute.");
            Console.WriteLine("PASS portable profiles open without installed game files while server start still validates them");
            return Task.CompletedTask;
        }
        finally
        {
            Environment.SetEnvironmentVariable("COI_GAME_DIR", originalGame);
            string resolved = Path.GetFullPath(directory);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Directory.Exists(resolved) && resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("coi-manager-profile-", StringComparison.Ordinal))
                Directory.Delete(resolved, recursive: true);
        }
    }
}
