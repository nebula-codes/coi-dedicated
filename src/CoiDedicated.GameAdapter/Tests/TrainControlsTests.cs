using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Bridges;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Trains;

internal static class TrainControlsTests
{
    internal static void Run()
    {
        var line=new TrainLineId(1000000);var network=new TrainNetworkId(2000000);var schedule=new TrainScheduleItemId(line,4095);
        IInputCommand[] valid={
            new RenameTrainLineCmd(line,"Iron route"),new RenameTrainNetworkCmd(network,"Main network"),
            new AssignTrainLineToTrainCmd(new TrainId(65535),TrainLineIdOrNone.None),
            new AssignTrainToNetworkCmd(new TrainId(65535),new TrainNetworkIdOrNone(network.Value)),
            new AddNewScheduleItemToTrainLineCmd(line,new EntityId(17),14,-1),
            new ReorderTrainLineScheduleItemCmd(schedule,0),new RemoveTrainLineScheduleItemCmd(schedule),
            new RemoveEntityFromScheduleItemCmd(schedule,new EntityId(17)),
            new SetTrainSchedulePriority(schedule,10,new EntityId(17)),
            new SetTrainScheduleSkipIfCannotArriveCmd(schedule,true),
            new SetTrainScheduleSkipIfNextCanArriveCmd(schedule,false),
            new SetTrainScheduleSkipIfHighFuelCmd(schedule,Percent.FromRaw(54321)),
            new ToggleLoadUnloadTrainScheduleItemCmd(schedule,true,false),
            new ToggleTrainStationModuleLoadUnloadCmd(new EntityId(17),null),
            new SetNetworkRefuelThresholdCmd(network,Percent.FromRaw(100000)),
            new RemoveTrainLineCmd(line),new RemoveTrainNetworkCmd(network)
        };
        var resolver=new DependencyResolverBuilder().BuildAndClear();
        foreach(var cmd in valid)
        {
            Check(GameCommandCodec.TryEncode(cmd,out byte[] wire,out string reason),reason);
            bool isolated=cmd is AddNewScheduleItemToTrainLineCmd or RemoveEntityFromScheduleItemCmd
                or SetTrainSchedulePriority or RemoveTrainLineScheduleItemCmd;
            Check(GameCommandCodec.RequiresIsolatedTick(wire)==isolated,"Unexpected schedule admission policy: "+cmd.GetType().Name);
            for(int i=0;i<wire.Length;i++)Reject(()=>GameCommandCodec.Decode(wire.AsMemory(0,i),resolver));
            Reject(()=>GameCommandCodec.Decode(wire.Concat(new byte[]{0}).ToArray(),resolver));
        }
        IInputCommand[] invalid={
            new RenameTrainLineCmd(line,"Invalid\nname"),new ReorderTrainLineScheduleItemCmd(schedule,-1),
            new AddNewScheduleItemToTrainLineCmd(line,new EntityId(17),15),
            new SetNetworkRefuelThresholdCmd(network,Percent.FromRaw(100001)),
            new SetTrainScheduleSkipIfHighFuelCmd(schedule,Percent.FromRaw(-1)),
            new CreateBridgeFromPlanCmd(new BridgePlan(ImmutableArray<BridgePlanStep>.Empty)),
            new CreateTrainTrackFromPlanCmd(new TrainTrackPlan(ImmutableArray<TrainTrackPlanStep>.Empty,true,true))
        };
        foreach(var cmd in invalid)Check(!GameCommandCodec.TryEncode(cmd,out _,out _),"Invalid command encoded: "+cmd.GetType().Name);
        // Train IDs use an unsigned 16-bit native type; rejecting before a cast prevents aliasing an existing train.
        using(var w=new PayloadWriter(128)){w.WriteUInt16(1);w.WriteUInt16(249);w.WriteInt32(65536);Reject(()=>GameCommandCodec.Decode(w.ToArray(),resolver));}
        // Bridge/rail counts are bounded before allocation/world lookup.
        using(var w=new PayloadWriter(128)){w.WriteUInt16(1);w.WriteUInt16(86);w.WriteUInt16(129);Reject(()=>GameCommandCodec.Decode(w.ToArray(),resolver));}
        using(var w=new PayloadWriter(128)){w.WriteUInt16(1);w.WriteUInt16(88);w.WriteInt32(3);w.WriteBoolean(false);w.WriteBoolean(false);w.WriteUInt16(129);Reject(()=>GameCommandCodec.Decode(w.ToArray(),resolver));}
    }
    private static void Reject(Action action){try{action();}catch(ProtocolException){return;}throw new Exception("Malformed train input accepted.");}
    private static void Check(bool v,string m){if(!v)throw new Exception(m);}
}
