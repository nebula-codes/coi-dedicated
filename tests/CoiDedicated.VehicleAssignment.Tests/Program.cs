using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Input;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Usage: VehicleAssignment.Tests SOURCE_SAVE NEW_ISOLATED_OUTPUT_DIR [GAME_DIR]");
        string source = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        string game = Path.GetFullPath(args.Length > 2 ? args[2] : Environment.GetEnvironmentVariable("COI_GAME_DIR")
            ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry");
        if (!File.Exists(source)) throw new FileNotFoundException("An existing CoI 0.8.7d fixture save is required.", source);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated output directory.");
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(output, "fixture.save");
        File.Copy(source, fixture);
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Captain of Industry_Data", "Managed", name.Name + ".dll")) : null;
        Environment.CurrentDirectory = game;
        return Run(fixture, output, game);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string fixture, string output, string game)
    {
        Assembly host = Assembly.Load("CoiDedicated");
        object options = host.GetType("CoiDedicated.HostOptions", true)!.GetMethod("Parse")!.Invoke(null,
            new object[] { new[] { "--game-dir", game, "--load", fixture, "--data-dir", output, "--steps", "0" } })!;
        Action<DependencyResolver, object> callback = (resolver, runner) => Test(resolver, runner, output);
        return (int)host.GetType("CoiDedicated.GameHost", true)!.GetMethod("Run")!.Invoke(null,
            new object[] { options, callback, CancellationToken.None })!;
    }

    private static void Test(DependencyResolver resolver, object runner, string output)
    {
        var entities = resolver.Resolve<EntitiesManager>();
        var scheduler = resolver.Resolve<IInputScheduler>();
        var zones = resolver.Resolve<LogisticsZonesManager>();
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner, Fix32.Zero);
        resolver.Resolve<Mafi.Core.Simulation.SimLoopEvents>().SetSimSpeed(1);
        var results = new List<object>();
        Vehicle[] vehicles = entities.GetAllEntitiesOfType<Vehicle>().ToArray();
        var harvester = vehicles.OfType<IEntityAssignedWithVehicles>()
            .FirstOrDefault(x => x.GetType().Name.Contains("TreeHarvester", StringComparison.Ordinal))
            ?? throw new Exception("Fixture requires an existing tree harvester.");
        var truckGroup = vehicles.Where(v => v.GetType().Name.Contains("Truck", StringComparison.Ordinal)
                && harvester.CanVehicleBeAssigned(v.Prototype))
            .GroupBy(v => v.Prototype).OrderByDescending(g => g.Count()).FirstOrDefault()
            ?? throw new Exception("Fixture requires compatible existing trucks.");
        Vehicle[] trucks = truckGroup.ToArray();
        Check(trucks.Length >= 2, "Fixture requires at least two trucks with the same prototype.");
        var proto = trucks[0].Prototype;
        Vehicle first = trucks[0], second = trucks[1];

        IInputCommand RoundTrip(IInputCommand original)
        {
            Check(GameCommandCodec.TryEncode(original, out byte[] wire, out string reason), reason);
            IInputCommand decoded = GameCommandCodec.Decode(wire, resolver);
            Check(decoded.GetType() == original.GetType(), "Command type changed in transit.");
            Check(GameCommandCodec.TryEncode(decoded, out byte[] rebuilt) && rebuilt.SequenceEqual(wire), "Command fields changed in transit.");
            Check(!decoded.ResultSet && !decoded.IsProcessed, "Decoded command has already been processed.");
            return decoded;
        }
        IInputCommand Execute(IInputCommand original)
        {
            IInputCommand decoded = RoundTrip(original);
            scheduler.ScheduleInputCmd(decoded);
            for (int i = 0; i < 2; i++) runner.GetType().GetMethod("Update")!.Invoke(runner, new object[] { 100.ToFix32() });
            Check(decoded.ResultSet && decoded.IsProcessedAndSynced && !decoded.HasError,
                "Native command failed: " + decoded.GetType().Name + ": " + decoded.ErrorMessage);
            return decoded;
        }
        Vehicle[] Assigned() => harvester.AllVehicles.AsEnumerable().ToArray();
        void ExpectLinks(params Vehicle[] expected)
        {
            Vehicle[] assigned = Assigned();
            Check(assigned.Length == expected.Length, $"Expected {expected.Length} assigned trucks; got {assigned.Length}.");
            Check(assigned.Select(v => v.Id.Value).Order().SequenceEqual(expected.Select(v => v.Id.Value).Order()), "Harvester's assigned vehicle IDs differ.");
            foreach (Vehicle truck in trucks)
            {
                bool belongs = expected.Contains(truck);
                Check(truck.AssignedTo.HasValue == belongs, "Vehicle backlink presence differs for " + truck.Id);
                if (belongs) Check(truck.AssignedTo.Value.Id == harvester.Id, "Vehicle points to the wrong target.");
            }
        }
        void Pass(string scenario)
        {
            results.Add(new { Scenario = scenario, Target = harvester.Id.Value, Prototype = proto.Id.Value,
                AssignedVehicleIds = Assigned().Select(v => v.Id.Value).Order().ToArray() });
            Console.WriteLine("PASS native vehicle assignment: " + scenario);
        }

        // Only the copied fixture is changed: release existing assignments so exact
        // counts and both sides of each ownership link can be checked deterministically.
        foreach (Vehicle truck in trucks) Execute(new UnassignVehicleCmd(truck.Id));
        foreach (Vehicle vehicle in Assigned()) Execute(new UnassignVehicleCmd(vehicle.Id));
        ExpectLinks();
        Execute(new AssignVehicleTypeToEntityCmd(proto, harvester, 2));
        Vehicle[] assignedByType = Assigned();
        Check(assignedByType.Length == 2, "Type assignment did not assign two trucks.");
        ExpectLinks(assignedByType);
        Pass("type-based count=2 assignment creates both ownership links");
        Execute(new UnassignVehicleFromEntityCmd(proto, harvester, 2));
        ExpectLinks();
        Pass("type-based count=2 removal clears both ownership links");

        Execute(new AssignVehicleToEntityCmd(first.Id, harvester.Id));
        ExpectLinks(first);
        Pass("specific vehicle-ID assignment creates both ownership links");
        Execute(new UnassignVehicleCmd(first.Id));
        ExpectLinks();
        Pass("specific vehicle-ID removal clears both ownership links");

        Check(zones.TryCreateZone(out LogisticsZone selectedZone), "Could not create isolated fixture zone.");
        Check(zones.TryCreateZone(out LogisticsZone excludedZone), "Could not create second isolated fixture zone.");
        foreach (Vehicle truck in trucks) truck.SetLogisticsZone(excludedZone);
        first.SetLogisticsZone(selectedZone);
        second.SetLogisticsZone(selectedZone);
        var withZone = (AssignVehicleTypeToEntityCmd)RoundTrip(new AssignVehicleTypeToEntityCmd(proto, harvester, 2, selectedZone.Id));
        Check(withZone.ZoneId.HasValue && withZone.ZoneId.Value == selectedZone.Id, "Assignment zone was lost.");
        Execute(withZone);
        ExpectLinks(first, second);
        Pass("explicit logistics-zone filter selects the two eligible trucks");
        var removalWithZone = (UnassignVehicleFromEntityCmd)RoundTrip(new UnassignVehicleFromEntityCmd(proto, harvester, 2, selectedZone.Id));
        Check(removalWithZone.ZoneToAssign.HasValue && removalWithZone.ZoneToAssign.Value == selectedZone.Id, "Removal zone was lost.");
        Execute(removalWithZone);
        ExpectLinks();
        Check(first.AssignedZone.IsNone && second.AssignedZone.IsNone, "Native removal with a zone option should return trucks to the default zone.");
        Pass("optional removal zone survives transport and preserves native default-zone behavior");

        // IDs are monotonic; the active-zone limit is not an ID limit. Preserve a
        // missing large ID so the native processor retains its fallback semantics.
        var largeZone = new LogisticsZoneId(1000000);
        var large = (AssignVehicleTypeToEntityCmd)RoundTrip(new AssignVehicleTypeToEntityCmd(proto, harvester, 1, largeZone));
        Check(large.ZoneId.HasValue && large.ZoneId.Value == largeZone, "Large logistics-zone ID was altered.");
        Pass("large nonnegative optional zone ID round trips without slot-index truncation");

        var wrongTarget = entities.GetAllEntitiesOfType<StaticEntity>().First(x => x is not IEntityAssignedWithVehicles);
        RejectDecode(new AssignVehicleTypeToEntityCmd(proto.Id, wrongTarget.Id, 1), resolver);
        RejectDecode(new UnassignVehicleFromEntityCmd(proto.Id, wrongTarget.Id, 1), resolver);
        RejectDecode(new AssignVehicleTypeToEntityCmd(proto.Id, new EntityId(int.MaxValue), 1), resolver);
        RejectDecode(new AssignVehicleTypeToEntityCmd(new DynamicEntityProto.ID("MissingVehicleForTest"), harvester.Id, 1), resolver);
        RejectDecode(new UnassignVehicleFromEntityCmd(new DynamicEntityProto.ID("MissingVehicleForTest"), harvester.Id, 1), resolver);
        Pass("missing and non-assignable targets plus unknown prototypes are rejected before scheduling");
        File.WriteAllText(Path.Combine(output, "vehicle-assignment-results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RejectDecode(IInputCommand command, DependencyResolver resolver)
    {
        Check(GameCommandCodec.TryEncode(command, out byte[] wire, out string reason), reason);
        try { GameCommandCodec.Decode(wire, resolver); }
        catch (ProtocolException) { return; }
        throw new Exception("Invalid command was accepted: " + command.GetType().Name);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
