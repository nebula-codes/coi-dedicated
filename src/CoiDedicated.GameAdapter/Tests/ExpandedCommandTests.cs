using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Input;

internal static class ExpandedCommandTests
{
    internal static void Run()
    {
        IInputCommand toggle = new StorageDisableLogisticsToggleCmd(new EntityId(17),true,false);
        Check(GameCommandCodec.TryEncode(toggle,out byte[] toggleBytes),"Storage toggle was rejected.");
        Check(Convert.ToHexString(toggleBytes)=="01007400110000000100","Storage toggle field fixture changed.");
        IInputCommand sliders = new StorageSetSliderStepCmd(new EntityId(17),(int?)5,(int?)null,(int?)0,(int?)10);
        Check(GameCommandCodec.TryEncode(sliders,out byte[] sliderBytes),"Storage slider rejected.");
        Check(Convert.ToHexString(sliderBytes)=="010046001100000005000000FFFFFFFF000000000A000000","Nullable slider sentinel was lost.");
        var resolver=new DependencyResolverBuilder().BuildAndClear();
        byte[] Payload(ushort kind,Action<PayloadWriter> content)
        {using var w=new PayloadWriter();w.WriteUInt16(1);w.WriteUInt16(kind);content(w);return w.ToArray();}
        var malformed=new[] {
            Payload(22,w=>{w.WriteInt32(4);w.WriteInt32(4);w.WriteInt32(int.MaxValue);w.WriteInt32(4);w.WriteBoolean(true);w.WriteBoolean(false);}),
            Payload(60,w=>{w.WriteString("Mining");w.WriteUInt16(1025);}),
            Payload(61,w=>{w.WriteUInt16(1);w.WriteInt32(5);w.WriteInt32(4);}),
            Payload(62,w=>{w.WriteInt32(17);w.WriteUInt16(257);}),
            Payload(70,w=>{w.WriteInt32(17);w.WriteInt32(11);w.WriteInt32(-1);w.WriteInt32(0);w.WriteInt32(10);}),
            Payload(71,w=>{w.WriteInt32(17);w.WriteBoolean(false);w.WriteInt32(-1);}),
            Payload(73,w=>{w.WriteInt32(17);w.WriteString("Truck");w.WriteInt32(int.MaxValue);}),
            Payload(77,w=>{w.WriteInt32(17);w.WriteString("bad\nname");}),
            Payload(116,w=>{w.WriteInt32(17);w.WriteBoolean(true);w.WriteUInt16(2);})
        };
        foreach(byte[] bytes in malformed)Reject(()=>GameCommandCodec.Decode(bytes,resolver));
        foreach(byte[] bytes in new[]{toggleBytes,sliderBytes})
        {
            for(int length=0;length<bytes.Length;length++)Reject(()=>GameCommandCodec.Decode(bytes.AsMemory(0,length),resolver));
            Reject(()=>GameCommandCodec.Decode(bytes.Concat(new byte[]{0}).ToArray(),resolver));
        }
        var pivots=new ImmutableArray<Tile3i>(new[]{new Tile3i(4,4,3),new Tile3i(12,4,3)});
        foreach(var direction in new[]{new Direction903d(-1),new Direction903d(6)})
            Check(!GameCommandCodec.TryEncode(new BuildTransportCmd(new StaticEntityProto.ID("PipeT1"),pivots,ImmutableArray<Tile2i>.Empty,direction,null,false,false),out _),"Invalid direction accepted.");
        Check(!GameCommandCodec.TryEncode(new BuildTransportCmd(new StaticEntityProto.ID("PipeT1"),pivots,ImmutableArray<Tile2i>.Empty,null,null,false,true),out _),"Free transport accepted.");
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Reject(Action action){try{action();}catch(ProtocolException){return;}throw new Exception("Expected invalid payload rejection.");}
}
