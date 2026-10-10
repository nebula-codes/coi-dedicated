using Mafi;
using Mafi.Base;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Game;
using Mafi.Core.Messages.Goals;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;

namespace CoiDedicated;

internal static class GameConfiguration
{
    // The default omits the starting factory for the bounded bootstrap fixture.
    // Enable it explicitly for an ordinary new island once bootstrap is verified.
    internal static ImmutableArray<IConfig> CreateConfigs(string mapPath, bool buildStartingFactory = false)
    {
        return new ImmutableArray<IConfig>(new IConfig[]
        {
            new WorldRegionMapFactoryConfig(
                typeof(FileWorldRegionMapFactory),
                Option<object>.Create(new FileWorldRegionMapFactory.Config(mapPath))),
            GameDifficultyConfig.Normal(),
            TutorialsConfig.CreateConfig(areTutorialsEnabled: false),
            new GameNameConfig("Dedicated Island"),
            new RandomSeedConfig("coi-dedicated-bootstrap"),
            new StartingLocationConfig(0),
            // A cached reload changed terrain flags in the standalone probe.
            // Persist the no-cache choice for newly created server worlds.
            new TerrainManagerConfig { MapCacheDisabled = true },
            new CoreModConfig { DisableSimulationBackgroundThread = true },
            new BaseModConfig { BuildStartingFactory = buildStartingFactory }
        });
    }
}
