using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Input;
using Mafi.Core.Vehicles.Commands;

internal static class VehicleAssignmentTests
{
    internal static void Run()
    {
        var truck = new DynamicEntityProto.ID("Truck");
        var target = new EntityId(17);
        var fixtures = new (IInputCommand Command, string Wire)[]
        {
            (new AssignVehicleTypeToEntityCmd(truck,target), "01004F001100000005000000547275636B0100000000"),
            (new UnassignVehicleFromEntityCmd(truck,target,2), "010050001100000005000000547275636B0200000000"),
            (new AssignVehicleTypeToEntityCmd(truck,target,128,new LogisticsZoneId(0)), "01004F001100000005000000547275636B800000000100000000"),
            // Zone IDs may exceed the number of simultaneously active zones.
            (new UnassignVehicleFromEntityCmd(truck,target,1,new LogisticsZoneId(65536)), "010050001100000005000000547275636B010000000100000100")
        };
        var emptyWorld = new DependencyResolverBuilder().BuildAndClear();
        foreach (var fixture in fixtures)
        {
            Check(GameCommandCodec.TryEncode(fixture.Command,out byte[] wire,out string reason), reason);
            Check(Convert.ToHexString(wire)==fixture.Wire,"Vehicle assignment wire fixture changed.");
            for(int length=0;length<wire.Length;length++)
                Reject(()=>GameCommandCodec.Decode(wire.AsMemory(0,length),emptyWorld));
            Reject(()=>GameCommandCodec.Decode(wire.Concat(new byte[]{0}).ToArray(),emptyWorld));
        }
        foreach(ushort kind in new ushort[]{79,80})
        {
            byte[] Payload(int count,byte zoneFlag,int zone=0)
            {
                using var writer=new PayloadWriter();
                writer.WriteUInt16(1);writer.WriteUInt16(kind);writer.WriteInt32(17);
                writer.WriteString("Truck");writer.WriteInt32(count);writer.WriteBoolean(zoneFlag!=0);
                if(zoneFlag==1)writer.WriteInt32(zone);
                byte[] payload=writer.ToArray();
                payload[payload.Length-(zoneFlag==1?5:1)]=zoneFlag;
                return payload;
            }
            foreach(int count in new[]{int.MinValue,-1,0,129,int.MaxValue})
                Reject(()=>GameCommandCodec.Decode(Payload(count,0),emptyWorld));
            Reject(()=>GameCommandCodec.Decode(Payload(1,2),emptyWorld));
            Reject(()=>GameCommandCodec.Decode(Payload(1,1,-1),emptyWorld));
        }
        Check(!GameCommandCodec.TryEncode(new AssignVehicleTypeToEntityCmd(truck,target,129),out _),"Oversized assignment accepted.");
        Check(!GameCommandCodec.TryEncode(new UnassignVehicleFromEntityCmd(truck,target,129),out _),"Oversized removal accepted.");
    }

    private static void Check(bool value,string reason) { if(!value)throw new Exception(reason); }
    private static void Reject(Action action)
    {
        try { action(); } catch(ProtocolException) { return; }
        throw new Exception("Expected malformed vehicle assignment rejection before world lookup.");
    }
}
