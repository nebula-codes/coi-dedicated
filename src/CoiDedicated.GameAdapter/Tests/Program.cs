using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Input;
using Mafi.Core.Simulation;

internal static class Program
{
    private static int Main(string[] args)
    {
        string gameDir = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("COI_GAME_DIR")
            ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry";
        string managed = Path.Combine(Path.GetFullPath(gameDir), "Captain of Industry_Data", "Managed");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            if (name.Name is not ("Mafi" or "Mafi.Core" or "Mafi.Base")) return null;
            string path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Environment.CurrentDirectory = Path.GetFullPath(gameDir);
        return Tests.Run();
    }
}

internal static class Tests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run()
    {
        var tests = new (string Name, Action Test)[]
        {
            ("Seven command encoders match explicit wire fixtures", EncodeFixtures),
            ("Pause and speed decode without a game world", DecodeScalarCommands),
            ("Unknown, invalid, truncated, and trailing payloads rejected", RejectMalformed),
            ("Unsupported subclasses and unsafe scalar options rejected", RejectUnsupported),
            ("Entity commands reject missing live entities", RejectMissingEntities),
            ("Building placement rejects malformed transforms before world lookup", RejectMalformedPlacement),
            ("Game build identity matches fixed fixture and every DLL affects it", BuildIdentityFixture)
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} game-adapter tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void EncodeFixtures()
    {
        CheckBytes(new SetSimPauseStateCmd(true), 1, 0, 1, 0, 1);
        CheckBytes(new GameSpeedChangeCmd(20), 1, 0, 2, 0, 20, 0, 0, 0);
        CheckBytes(new SetConstructionPriorityCmd(new EntityId(17), 14), 1, 0, 3, 0, 17, 0, 0, 0, 14, 0, 0, 0);
        CheckBytes(new MachineSetRecipeActiveCmd(new EntityId(17), new RecipeProto.ID("Iron"), true),
            1, 0, 4, 0, 17, 0, 0, 0, 4, 0, 0, 0, 73, 114, 111, 110, 1);
        CheckBytes(new StartDeconstructionOfStaticEntityCmd(new EntityId(17), EntityRemoveReason.Remove, true),
            1, 0, 5, 0, 17, 0, 0, 0, 1);
        CheckBytes(new ToggleStaticEntityConstructionCmd(new EntityId(17)), 1, 0, 6, 0, 17, 0, 0, 0);
        CheckBytes(new CreateStaticEntityCmd(new StaticEntityProto.ID("Iron"),
                new TileTransform(new Tile3i(17, 18, 3), Rotation90.Deg90, true)),
            1, 0, 7, 0, 4, 0, 0, 0, 73, 114, 111, 110,
            17, 0, 0, 0, 18, 0, 0, 0, 3, 0, 0, 0, 1, 0, 0, 0, 1);
        CheckBytes(new CreateStaticEntityCmd(new StaticEntityProto.ID("StorageUnit"),
                new TileTransform(new Tile3i(452, 1405, 3))),
            Convert.FromHexString("010007000B00000053746F72616765556E6974C40100007D050000030000000000000000"));
    }

    private static void DecodeScalarCommands()
    {
        var resolver = new DependencyResolverBuilder().BuildAndClear();
        var pause = (SetSimPauseStateCmd)GameCommandCodec.Decode(new byte[] { 1, 0, 1, 0, 1 }, resolver);
        Check(pause.IsPaused && !pause.IsProcessed && !pause.ResultSet, "Pause command should be fresh and paused.");
        var speed = (GameSpeedChangeCmd)GameCommandCodec.Decode(new byte[] { 1, 0, 2, 0, 20, 0, 0, 0 }, resolver);
        Check(speed.NewSpeedMultiplier == 20 && !speed.IsProcessed, "Speed mismatch.");
    }

    private static void RejectMalformed()
    {
        var resolver = new DependencyResolverBuilder().BuildAndClear();
        byte[][] malformed =
        {
            new byte[] { 2, 0, 1, 0, 1 }, // Unknown version.
            new byte[] { 1, 0, 255, 255 }, // Unknown type.
            new byte[] { 1, 0, 1, 0, 2 }, // Noncanonical Boolean.
            new byte[] { 1, 0, 1, 0, 1, 0 }, // Trailing bytes.
            new byte[] { 1, 0, 2, 0, 0, 0, 0, 0 }, // Speed zero.
            new byte[] { 1, 0, 2, 0, 21, 0, 0, 0 }, // Speed above bound.
            new byte[] { 1, 0, 3, 0, 0, 0, 0, 0, 14, 0, 0, 0 }, // Invalid entity ID.
            new byte[] { 1, 0, 3, 0, 17, 0, 0, 0, 15, 0, 0, 0 }, // Invalid priority.
            new byte[] { 1, 0, 4, 0, 17, 0, 0, 0, 255, 255, 255, 255 }, // Unbounded string length.
            new byte[] { 1, 0, 4, 0, 17, 0, 0, 0, 1, 0, 0, 0, 255, 1 }, // Invalid UTF-8.
            new byte[GameCommandCodec.MaxCommandBytes + 1]
        };
        foreach (byte[] payload in malformed) Throws<ProtocolException>(() => GameCommandCodec.Decode(payload, resolver));
        byte[] valid = { 1, 0, 2, 0, 3, 0, 0, 0 };
        for (int length = 0; length < valid.Length; length++)
            Throws<ProtocolException>(() => GameCommandCodec.Decode(valid.AsMemory(0, length), resolver));
    }

    private static void RejectUnsupported()
    {
        IInputCommand[] commands =
        {
            new DerivedPause(),
            new GameSpeedChangeCmd(21),
            new SetConstructionPriorityCmd(new EntityId(17), -1),
            new SetConstructionPriorityCmd(new EntityId(0), 1),
            new MachineSetRecipeActiveCmd(new EntityId(17), new RecipeProto.ID(""), true),
            new MachineSetRecipeActiveCmd(new EntityId(17), new RecipeProto.ID("Bad\nId"), true),
            new StartDeconstructionOfStaticEntityCmd(new EntityId(17), EntityRemoveReason.Collapse),
            new CreateStaticEntityCmd(new StaticEntityProto.ID("Iron"), TileTransform.Identity, isFree: true),
            new CreateStaticEntityCmd(new StaticEntityProto.ID("Iron"), TileTransform.Identity, allowValidationSuppression: true),
            new CreateStaticEntityCmd(new StaticEntityProto.ID("Iron"), new TileTransform(new Tile3i(-1, 1, 1))),
            new CreateStaticEntityCmd(new StaticEntityProto.ID("Iron"), new TileTransform(new Tile3i(1, 1, short.MaxValue + 1))),
            new DerivedCreate()
        };
        foreach (IInputCommand command in commands)
        {
            Check(!GameCommandCodec.TryEncode(command, out byte[] payload, out string reason), "Invalid command was encoded.");
            Check(payload.Length == 0 && !string.IsNullOrWhiteSpace(reason), "Rejected command must explain failure and have no payload.");
        }
    }

    private static void RejectMalformedPlacement()
    {
        var resolver = new DependencyResolverBuilder().BuildAndClear();
        byte[] Build(int x, int y, int z, int rotation, string id = "Iron")
        {
            using var writer = new PayloadWriter(GameCommandCodec.MaxCommandBytes);
            writer.WriteUInt16(GameCommandCodec.Version);
            writer.WriteUInt16((ushort)GameCommandKind.CreateBuilding);
            writer.WriteString(id, GameCommandCodec.MaxProtoIdBytes);
            writer.WriteInt32(x); writer.WriteInt32(y); writer.WriteInt32(z); writer.WriteInt32(rotation);
            writer.WriteBoolean(false);
            return writer.ToArray();
        }
        foreach (byte[] invalid in new[] {
            Build(-1, 18, 3, 0), Build(17, 32768, 3, 0), Build(17, 18, int.MaxValue, 0),
            Build(17, 18, int.MinValue, 0), Build(17, 18, 3, -1), Build(17, 18, 3, 4),
            Build(17, 18, 3, 0, ""), Build(17, 18, 3, 0, "Bad\nId") })
            Throws<ProtocolException>(() => GameCommandCodec.Decode(invalid, resolver));
        byte[] valid = Build(17, 18, 3, 0);
        for (int length = 0; length < valid.Length; length++)
            Throws<ProtocolException>(() => GameCommandCodec.Decode(valid.AsMemory(0, length), resolver));
        Throws<ProtocolException>(() => GameCommandCodec.Decode(valid.Concat(new byte[] { 1 }).ToArray(), resolver));
        valid[valid.Length - 1] = 2;
        Throws<ProtocolException>(() => GameCommandCodec.Decode(valid, resolver));
    }

    private static void RejectMissingEntities()
    {
        var manager = DispatchProxy.Create<IEntitiesManager, MissingEntitiesProxy>();
        var builder = new DependencyResolverBuilder();
        builder.RegisterInstance(manager).As<IEntitiesManager>();
        var resolver = builder.BuildAndClear();
        IInputCommand[] commands =
        {
            new SetConstructionPriorityCmd(new EntityId(17), 7),
            new MachineSetRecipeActiveCmd(new EntityId(17), new RecipeProto.ID("Iron"), true),
            new StartDeconstructionOfStaticEntityCmd(new EntityId(17), EntityRemoveReason.Remove),
            new ToggleStaticEntityConstructionCmd(new EntityId(17))
        };
        foreach (IInputCommand command in commands)
        {
            Check(GameCommandCodec.TryEncode(command, out byte[] payload), "Expected encodable reference.");
            Throws<ProtocolException>(() => GameCommandCodec.Decode(payload, resolver));
        }
    }

    private static void CheckBytes(IInputCommand command, params byte[] expected)
    {
        Check(GameCommandCodec.TryEncode(command, out byte[] actual, out string reason), reason);
        Check(actual.SequenceEqual(expected), $"Wire fixture mismatch for {command.GetType().Name}.");
    }

    private static void BuildIdentityFixture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "coi-build-identity-" + Guid.NewGuid().ToString("N"));
        string[] names = { "Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll" };
        string[] values = { "mafi", "core", "base" };
        Directory.CreateDirectory(directory);
        try
        {
            for (int i = 0; i < names.Length; i++) File.WriteAllText(Path.Combine(directory, names[i]), values[i]);
            const string expected = "CoI 0.8.7d / dedicated-prototype-2 / cmd1 / 10d29925e454831bda13ad914a68598350c19ad94721b1022dc37c2f956e9454";
            Check(GameBuildIdentity.Compute(directory) == expected, "Combined identity did not match the independent SHA-256 fixture.");
            Check(System.Text.Encoding.UTF8.GetByteCount(expected) <= 128, "Build identity exceeds handshake limit.");
            for (int i = 0; i < names.Length; i++)
            {
                string path = Path.Combine(directory, names[i]);
                File.AppendAllText(path, "changed");
                Check(GameBuildIdentity.Compute(directory) != expected, "Changing " + names[i] + " did not change identity.");
                File.WriteAllText(path, values[i]);
            }
            File.Delete(Path.Combine(directory, names[2]));
            Throws<FileNotFoundException>(() => GameBuildIdentity.Compute(directory));
        }
        finally
        {
            foreach (string name in names) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private sealed class DerivedPause : SetSimPauseStateCmd
    {
        public DerivedPause() : base(true) { }
    }

    private sealed class DerivedCreate : CreateStaticEntityCmd
    {
        public DerivedCreate() : base(new StaticEntityProto.ID("Iron"), TileTransform.Identity) { }
    }
}

public class MissingEntitiesProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "TryGetEntity")
        {
            args![1] = null;
            return false;
        }
        throw new NotSupportedException(targetMethod?.Name);
    }
}
