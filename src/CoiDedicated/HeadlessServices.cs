using Mafi;
using Mafi.Core.Game;
using Mafi.Core.Messages;
using Mafi.Core.SaveGame;
using Mafi.Core.Terrain.Generation;

namespace CoiDedicated;

internal static class HeadlessServices
{
    internal static void Register(DependencyResolverBuilder builder)
    {
        // The normal tracker stores a player's read history in Unity PlayerPrefs.
        // Use the game's stateless fallback; TutorialsConfig separately disables
        // tutorial goals. Returning false here would unlock every message at start.
        builder.ClearRegistrations<ITutorialProgressTracker>();
        builder.RegisterInstance(new AlwaysNewTutorialProgressTracker())
            .As<ITutorialProgressTracker>();

        // Saving metadata can be produced from simulation state without rendering
        // a screenshot or consulting a local player's UI settings.
        builder.ClearRegistrations<ISaveConfig>();
        builder.RegisterInstance(new HeadlessSaveConfig()).As<ISaveConfig>();
        builder.ClearRegistrations<IGameSaveInfoProvider>();
        builder.RegisterDependency<SimpleGameSaveInfoProvider>().As<IGameSaveInfoProvider>();

        // GameNameConfig is session configuration and is omitted from saved
        // configs. Re-register it on both the new-world and restore paths.
        builder.ClearRegistrations<GameNameConfig>();
        builder.RegisterInstance(new GameNameConfig("Dedicated Island"))
            .AsSelf().AsAllInterfaces();

        // Existing saves preserve TerrainManager.MapCacheDisabled independently
        // of session configs. Use the game's no-cache service so those saves also
        // regenerate terrain instead of reading cache data with differing flags.
        builder.ClearRegistrations<IMapCacheManager>();
        builder.RegisterInstance(new NoMapCacheManager()).As<IMapCacheManager>();
    }

    private sealed class HeadlessSaveConfig : ISaveConfig
    {
        public SaveCompressionType SaveCompressionType => SaveCompressionType.Gzip;
        public Duration? AutoSaveInterval => null;
        public int? MaxAutoSavesCount => null;
    }
}
