using System.Reflection;
using System.Collections;
using Mafi;
using Mafi.Base;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Input;
using Mafi.Core.GameLoop;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;
using Mafi.Localization;

namespace CoiDedicated;

internal static class GameHost
{
    public static int Run(HostOptions options, Action<DependencyResolver, object>? session = null)
    {
        LocalizationManager.MakeInvariantCultureDefault();
        Log.LogReceived += entry => Console.WriteLine(entry.ToString());
        foreach (string name in new[] { "Mafi", "Mafi.Core", "Mafi.Base" })
        {
            Assembly assembly = Assembly.Load(name);
            Console.WriteLine($"Loaded {assembly.GetName().Name} {assembly.GetName().Version}: {assembly.GetTypes().Length} types");
        }
        // This call also exercises the game's static initialization outside Unity.
        Console.WriteLine($"Fixed-point arithmetic: {(1.ToFix32() + 2.ToFix32()).ToString()}");
        if (options.Inspect) return 0;
        var fs = new FileSystemHelper(options.DataDirectory, options.GameDirectory);
        Assembly coreAssembly = typeof(CoreMod).Assembly;
        Assembly baseAssembly = typeof(BaseMod).Assembly;
        var mods = new ImmutableArray<LoadedModData>(new[] {
            MakeMod("COI-Core", typeof(CoreMod), options.GameDirectory, coreAssembly, typeof(Fix32).Assembly),
            MakeMod("COI-CoreData", typeof(BaseMod), options.GameDirectory, baseAssembly)
        });
        DependencyResolver? resolver = null;
        Type builder = coreAssembly.GetType("Mafi.Core.Game.GameBuilder", true)!;
        Type runnerType = coreAssembly.GetType("Mafi.Core.GameLoop.GameRunner", true)!;
        Action<DependencyResolver> onResolver = created => resolver = created;
        string mapPath = Path.Combine(options.GameDirectory, "Maps", options.Map + ".map");
        if (options.Load is null && !File.Exists(mapPath)) throw new FileNotFoundException("Map not found", mapPath);
        var configs = GameConfiguration.CreateConfigs(mapPath, options.StartingFactory);
        using FileStream? loadStream = options.Load is null ? null : File.OpenRead(options.Load);
        MethodInfo build = builder.GetMethod(options.Load is null ? "BuildNewGameTimeSliced" : "BuildLoadedGameTimeSliced", BindingFlags.Public | BindingFlags.Static)!;
        object?[] parameters = build.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        parameters[0] = loadStream is null ? new StartNewGameArgs(mods, configs, fs) : new LoadGameArgs(loadStream, fs, forceSynchronousLoading: true);
        parameters[1] = false;
        if (loadStream is null)
        {
            parameters[2] = onResolver;
            parameters[7] = new Action<DependencyResolverBuilder, ProtosDb, ImmutableArray<IMod>, Mafi.Collections.Set<Assembly>>((deps, _, _, _) => HeadlessServices.Register(deps));
        }
        else
        {
            parameters[2] = new Func<ImmutableArray<ModInfoRaw>, ImmutableArray<LoadedModData>>(requested =>
            {
                foreach (var mod in requested)
                    if (mod.Id != "COI-Core" && mod.Id != "COI-CoreData")
                        throw new NotSupportedException($"This prototype cannot load save dependency {mod.Id}.");
                return mods;
            });
            parameters[3] = onResolver;
            parameters[8] = new Action<DependencyResolverBuilder, ProtosDb, ImmutableArray<IMod>>((deps, _, _) => HeadlessServices.Register(deps));
            parameters[4] = new Action<ImmutableArray<IConfig>>(loaded =>
            {
                foreach (IConfig config in loaded)
                {
                    if (config is IGameRunnerConfig runnerConfig) runnerConfig.DisableSimulationBackgroundThread = true;
                    if (config is TerrainManagerConfig terrainConfig) terrainConfig.MapCacheDisabled = true;
                }
            });
        }
        var initialization = (IEnumerator)build.Invoke(null, parameters)!;
        while (initialization.MoveNext()) Console.WriteLine($"[init] {initialization.Current}");
        // The load is synchronous. Release the input before a server replaces
        // that same save path during autosave (Windows denies it while open).
        loadStream?.Dispose();
        if (resolver is null) throw new InvalidOperationException("The game did not supply a resolver.");
        object runner = resolver.Resolve(runnerType);
        MethodInfo update = runnerType.GetMethod("Update")!;
        MethodInfo terminate = runnerType.GetMethod("Terminate")!;
        ISimLoopEvents simulation = resolver.Resolve<ISimLoopEvents>();
        int start = simulation.CurrentStep.Value;
        try
        {
            Console.WriteLine($"[server] Initialized at simulation step {start}");
            if (session is not null)
            {
                session(resolver, runner);
                return 0;
            }
            if (options.Serve)
            {
                DedicatedService.Run(options, resolver, runner);
                return 0;
            }
            // Service and peer sessions order their own resume command. A bounded
            // local simulation resumes through the ordinary game input queue.
            resolver.Resolve<IInputScheduler>().ScheduleInputCmd(new SetSimPauseStateCmd(false));
            for (int i = 0; i < options.Steps; i++) update.Invoke(runner, new object[] { 100.ToFix32() });
            Console.WriteLine($"[server] Advanced to step {simulation.CurrentStep.Value}");
            DedicatedService.Save(options, resolver);
            string report = System.Text.Json.JsonSerializer.Serialize(GameWorldStatus.Read(resolver, includeTerrain: true), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(options.DataDirectory, "world-status.json"), report);
        }
        finally { terminate.Invoke(runner, null); }
        return 0;
    }

    private static LoadedModData MakeMod(string id, Type type, string root, params Assembly[] assemblies) =>
        new(new ModManifest(root, id, new VersionSlim(0, 8, 7), id, ImmutableArray<string>.Empty), type, new ImmutableArray<Assembly>(assemblies));
}
