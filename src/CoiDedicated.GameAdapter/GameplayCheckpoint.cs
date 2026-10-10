using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mafi;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Products;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;
using Mafi.Core.Terrain.Props;

namespace CoiDedicated.GameAdapter;

/// <summary>
/// Canonical selected gameplay state, not a complete save hash. Covers entity identity,
/// static positions and enabled/paused flags; all global product totals; population;
/// terrain contents, queues and props; step, pause and speed. Excludes UI/session state.
/// Matching checkpoints do not prove equality of research, routes, buffers or every entity field.
/// </summary>
public static class GameplayCheckpoint
{
    public const int Schema = 1;
    public const int IntervalTicks = 300;

    // Only call between native updates, on the simulation thread.
    public static byte[] Compute(DependencyResolver resolver) => Compute(resolver, out _);

    /// <summary>Diagnostic overload reports the native complete terrain scan cost, without changing hash contents.</summary>
    public static byte[] Compute(DependencyResolver resolver, out double terrainMilliseconds)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Schema);
            var simulation = resolver.Resolve<ISimLoopEvents>();
            writer.Write(simulation.CurrentStep.Value);
            writer.Write(simulation.IsSimPaused);
            writer.Write(simulation.SimSpeedMult);
            var entities = resolver.Resolve<EntitiesManager>().GetAllEntitiesOfType<IEntity>().OrderBy(e => e.Id.Value).ToArray();
            writer.Write(entities.Length);
            foreach (IEntity entity in entities)
            {
                writer.Write(entity.Id.Value);
                writer.Write(entity.Prototype.Id.Value);
                writer.Write(entity.IsEnabled);
                writer.Write(entity.IsPaused);
                writer.Write(entity.IsDestroyed);
                writer.Write(entity is StaticEntity);
                if (entity is StaticEntity stationary)
                {
                    writer.Write(stationary.CenterTile.X); writer.Write(stationary.CenterTile.Y); writer.Write(stationary.CenterTile.Z);
                }
            }
            var products = resolver.Resolve<IProductsManager>().ProductStats.OrderBy(p => p.Product.Id.Value, StringComparer.Ordinal).ToArray();
            writer.Write(products.Length);
            foreach (ProductStats product in products)
            {
                writer.Write(product.Product.Id.Value);
                writer.Write(product.GlobalQuantity.Value);
                writer.Write(product.StoredAvailableQuantity.Value);
                writer.Write(product.StoredUnavailableQuantity.Value);
                writer.Write(product.StorageCapacity.Value);
            }
            var population = resolver.Resolve<SettlementsManager>();
            writer.Write(population.GetTotalPopulation()); writer.Write(population.SettlementsCount);
            writer.Write(population.NumberOfHomeless); writer.Write(population.TotalHousingCapacity);
            var terrain = resolver.Resolve<TerrainManager>();
            var terrainClock = System.Diagnostics.Stopwatch.StartNew();
            var fingerprint = TerrainFingerprint.Compute(ref terrain.GetMutableTerrainDataRef());
            terrainClock.Stop();
            terrainMilliseconds = terrainClock.Elapsed.TotalMilliseconds;
            writer.Write(fingerprint.ToString());
            writer.Write(terrain.GetPhysicsProcessingQueueSize()); writer.Write(terrain.GetDisruptionQueueSize());
            var props = resolver.Resolve<TerrainPropsManager>();
            writer.Write(props.PropsCount); writer.Write(props.RemovedPropsCount);
        }
        using var sha = SHA256.Create();
        bytes.Position = 0;
        return sha.ComputeHash(bytes);
    }
}
