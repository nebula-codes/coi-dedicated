using Mafi;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Entities;
using Mafi.Core.Products;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;
using Mafi.Core.Terrain.Props;

namespace CoiDedicated;

internal static class GameWorldStatus
{
    // Call between updates on the host's simulation thread, after initialization.
    // These selected counters are diagnostics, not a complete simulation state hash.
    internal static GameWorldSnapshot Read(DependencyResolver resolver, bool includeTerrain = false)
    {
        var simulation = resolver.Resolve<ISimLoopEvents>();
        Type runnerType = typeof(ISimLoopEvents).Assembly.GetType("Mafi.Core.GameLoop.GameRunner", throwOnError: true)!;
        object runner = resolver.Resolve(runnerType);
        Fix64 duration = (Fix64)runnerType.GetProperty("GameDurationMs")!.GetValue(runner)!;
        var entities = resolver.Resolve<EntitiesManager>();
        var settlements = resolver.Resolve<SettlementsManager>();
        var products = resolver.Resolve<IProductsManager>();
        var terrain = resolver.Resolve<TerrainManager>();
        var props = resolver.Resolve<TerrainPropsManager>();
        var quantities = new List<ProductQuantitySnapshot>();

        foreach (ProductStats stats in products.ProductStats)
        {
            if (stats.GlobalQuantity.IsZero && stats.StoredAvailableQuantity.IsZero
                && stats.StoredUnavailableQuantity.IsZero && stats.StorageCapacity.IsZero)
                continue;

            quantities.Add(new ProductQuantitySnapshot(
                stats.Product.Id.Value,
                stats.GlobalQuantity.Value,
                stats.StoredAvailableQuantity.Value,
                stats.StoredUnavailableQuantity.Value,
                stats.StorageCapacity.Value));
        }

        quantities.Sort((left, right) => StringComparer.Ordinal.Compare(left.ProductId, right.ProductId));
        return new GameWorldSnapshot(
            simulation.CurrentStep.Value,
            duration.ToString(),
            duration.RawValue,
            entities.EntitiesCount,
            settlements.GetTotalPopulation(),
            settlements.SettlementsCount,
            settlements.NumberOfHomeless,
            settlements.TotalHousingCapacity,
            quantities.ToArray(),
            includeTerrain ? new TerrainSnapshot(
                terrain.InitialTerrainFingerprint.ToString(),
                TerrainFingerprint.Compute(ref terrain.GetMutableTerrainDataRef()).ToString(),
                terrain.GetPhysicsProcessingQueueSize(),
                terrain.GetDisruptionQueueSize(),
                props.PropsCount,
                props.RemovedPropsCount) : null);
    }

    // Diagnostic serialization, using the same raw stream API as the game's DeterminismValidator.
    // Equal digests are stronger evidence than selected counters; unequal digests require layout analysis.
    internal static RawStateSnapshot WriteRawState(DependencyResolver resolver, string output)
    {
        using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
            resolver.Resolve<Mafi.Core.SaveGame.SaveManager>().SaveGameRaw(stream);
        using var source = File.OpenRead(output);
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)).ToLowerInvariant();
        return new RawStateSnapshot(source.Length, digest);
    }
}

internal sealed record GameWorldSnapshot(
    int SimStep,
    string GameDurationMs,
    long GameDurationMsRaw,
    int EntityCount,
    int Population,
    int SettlementCount,
    int Homeless,
    int HousingCapacity,
    ProductQuantitySnapshot[] Products,
    TerrainSnapshot? Terrain);

internal sealed record RawStateSnapshot(long Length, string Sha256);

internal sealed record TerrainSnapshot(
    string InitialFingerprint,
    string CurrentFingerprint,
    int PhysicsQueueSize,
    int DisruptionQueueSize,
    int PropsCount,
    int RemovedPropsCount);

// Quantities use each product's game units; GlobalQuantity excludes terrain deposits.
// Products with all four quantities zero are omitted from the snapshot.
internal sealed record ProductQuantitySnapshot(
    string ProductId,
    long GlobalQuantity,
    long StoredAvailableQuantity,
    long StoredUnavailableQuantity,
    long StorageCapacity);
