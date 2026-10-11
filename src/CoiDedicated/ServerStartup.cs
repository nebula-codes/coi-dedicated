using System.Collections.Concurrent;
using Mafi;

namespace CoiDedicated;

/// <summary>Process-local dedicated startup for an exact reviewed game build.</summary>
internal static class ServerStartup
{
    internal static void Initialize(string gameDirectory)
    {
        // The direct GameHost entry is also used by isolated simulation tools.
        ServerGameCompatibility.Validate(gameDirectory);
        // A direct caller may already have bound assemblies from another path.
        // Verify the actual runtime files too, before touching native state.
        foreach (string name in new[] { "Mafi", "Mafi.Core", "Mafi.Base" })
            ServerGameCompatibility.ValidateLoadedAssembly(System.Reflection.Assembly.Load(name));

        // CoI 0.8.7d's transports initializer reads this diagnostic collection
        // before registering its delayed ownership-failure callback. Supply the
        // known startup state only in our dedicated process. This is deliberately
        // not a Steam login/subscription result, and does not change game files,
        // normal gameplay callbacks, client startup, or player/control keys.
        if (Log.DebugInfos is not ConcurrentBag<string> startupState)
            throw new NotSupportedException("The reviewed game startup-state layout changed; no island was loaded.");
        const string marker = "Steam client connected";
        if (!startupState.Contains(marker)) startupState.Add(marker);
        Console.WriteLine($"[host] Server-only startup compatibility applied for CoI {ServerGameCompatibility.SupportedVersion}; Steam is not initialized.");
    }
}
