using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace CoiDedicated;

/// <summary>Completes CoI's Steam startup contract before any world is loaded.</summary>
internal sealed class NativeSteamSession : IDisposable
{
    private static readonly HashSet<Assembly> NativeResolvers = new();
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _resolve;
    private MethodInfo? _shutdown;

    private NativeSteamSession(string gameDirectory)
    {
        string managed = Path.Combine(gameDirectory, "Captain of Industry_Data", "Managed");
        _resolve = (context, name) => name.Name is "Facepunch.Steamworks.Win64"
            ? context.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
        AssemblyLoadContext.Default.Resolving += _resolve;
    }

    internal static NativeSteamSession Start(string gameDirectory)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new NotSupportedException("Server startup requires Windows x64 and a running Steam client signed into an account that owns Captain of Industry. The current Linux/Docker host cannot perform the game's required Steam validation; no island was loaded.");

        var session = new NativeSteamSession(gameDirectory);
        try
        {
            string nativePath = Path.Combine(gameDirectory, "Captain of Industry_Data", "Plugins", "x86_64", "steam_api64.dll");
            if (!File.Exists(nativePath)) throw new FileNotFoundException("The installed game's Steam library is missing. Verify Captain of Industry's files in Steam.", nativePath);
            if (File.Exists(Path.Combine(Environment.CurrentDirectory, "steam_appid.txt")))
                throw new InvalidOperationException("A steam_appid.txt override is present in the working directory. Use the normal Steam installation without an application-ID override.");
            Assembly steam = Assembly.Load("Facepunch.Steamworks.Win64");
            lock (NativeResolvers)
                if (NativeResolvers.Add(steam))
                    NativeLibrary.SetDllImportResolver(steam, (name, _, _) => name is "steam_api64" or "steam_api64.dll"
                        ? NativeLibrary.Load(nativePath) : IntPtr.Zero);

            Type client = steam.GetType("Steamworks.SteamClient", throwOnError: true)!;
            Type apps = steam.GetType("Steamworks.SteamApps", throwOnError: true)!;
            session._shutdown = client.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(client.FullName, "Shutdown");
            MethodInfo initialize = client.GetMethod("Init", new[] { typeof(uint), typeof(bool) })
                ?? throw new MissingMethodException(client.FullName, "Init");

            // GameValidator.Initialize's VM interpreter is incompatible with .NET.
            // Perform its real Steam calls through the unchanged shipped wrapper.
            // On failure, stop here rather than launching/restarting a game process.
            initialize.Invoke(null, new object[] { 1594320u, true });
            bool valid = (bool)client.GetProperty("IsValid")!.GetValue(null)!;
            bool subscribed = valid && (bool)apps.GetProperty("IsSubscribed")!.GetValue(null)!;
            if (!valid || !subscribed)
                throw new InvalidOperationException("Steam did not confirm an active client with a Captain of Industry subscription.");

            // CoI 0.8.7d consumes this exact successful-startup diagnostic when it
            // initializes world dependencies. Record it only after genuine Steam
            // initialization and subscription verification, as GameValidator does.
            MethodInfo diagnostic = typeof(Mafi.Log).GetMethod("InfoDebug", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null, types: new[] { typeof(string) }, modifiers: null)
                ?? throw new MissingMethodException(typeof(Mafi.Log).FullName, "InfoDebug");
            diagnostic.Invoke(null, new object[] { "Steam client connected" });
            Console.WriteLine("[host] Steam initialization and ownership verification succeeded before world initialization.");
            return session;
        }
        catch (Exception error)
        {
            session.Dispose();
            while (error is TargetInvocationException { InnerException: not null }) error = error.InnerException;
            throw new InvalidOperationException("Cannot initialize the installed game's Steam validation. The server stopped before loading an island. Keep Steam running with the owning account signed in. " + error.Message, error);
        }
    }

    public void Dispose()
    {
        MethodInfo? shutdown = Interlocked.Exchange(ref _shutdown, null);
        try { shutdown?.Invoke(null, null); }
        catch (Exception error) { Console.Error.WriteLine("[host] Steam shutdown: " + error.GetBaseException().Message); }
        finally { AssemblyLoadContext.Default.Resolving -= _resolve; }
    }
}
