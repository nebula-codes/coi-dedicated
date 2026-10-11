using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace CoiDedicated;

/// <summary>Checks genuine Steam startup without constructing or loading an island.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--game-dir")
                throw new ArgumentException("Usage: CoiDedicated.SteamCheck --game-dir GAME_DIRECTORY");
            string game = Path.GetFullPath(args[1]);
            string managed = Path.Combine(game, "Captain of Industry_Data", "Managed");
            if (!Directory.Exists(managed)) throw new DirectoryNotFoundException("The installed game Managed directory is missing.");
            AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "Mafi"
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "Mafi.dll")) : null;
            Environment.CurrentDirectory = game;
            return Validate(game);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetBaseException().Message);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Validate(string game)
    {
        using var session = NativeSteamSession.Start(game);
        Console.WriteLine("Steam validation passed. No island was loaded.");
        return 0;
    }
}
