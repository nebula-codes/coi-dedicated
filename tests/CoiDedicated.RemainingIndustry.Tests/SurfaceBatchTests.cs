using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

internal static class SurfaceBatchTests
{
    public static void Run()
    {
        var empty=new DependencyResolverBuilder().BuildAndClear();
        var data=new ImmutableArray<TileSurfaceCopyPasteData>(new[]{new TileSurfaceCopyPasteData(new TileSurfaceData((15<<19)|1,1|(7<<11)),new Tile2i(100,104),2,3)});
        IInputCommand[] values={new BatchAddSurfaceDecalCmd(data),new BatchRemoveSurfaceDecalCmd(data),new BatchRemoveSurfacePlacingDesignationsCmd(data),new PasteSurfaceDesignationsCmd(new Proto.ID("SurfacePlacing"),data)};
        int rejected=0;
        foreach(var value in values)
        {
            Check(GameCommandCodec.TryEncode(value,out var wire,out var reason),reason);
            for(int length=0;length<wire.Length;length++){Reject(()=>GameCommandCodec.Decode(wire.AsMemory(0,length),empty));rejected++;}
            Reject(()=>GameCommandCodec.Decode(wire.Concat(new byte[]{0}).ToArray(),empty));rejected++;
        }
        IInputCommand Invalid(int raw,int decal,int width=1,int height=1)=>new BatchAddSurfaceDecalCmd(new ImmutableArray<TileSurfaceCopyPasteData>(new[]{new TileSurfaceCopyPasteData(new TileSurfaceData(raw,decal),new Tile2i(0,0),width,height)}));
        var invalid=new[]{Invalid(1,0x4000),Invalid(1|0x8000,1),Invalid(1|0x4000,1),Invalid(32,1),Invalid(0x200,1),Invalid(1,0x200),Invalid(1,1,200,200),new BatchAddSurfaceDecalCmd(new ImmutableArray<TileSurfaceCopyPasteData>(new[]{data[0],data[0]}))};
        foreach(var value in invalid)Check(!GameCommandCodec.TryEncode(value,out _,out _),"Invalid surface batch encoded.");
        using(var w=new PayloadWriter(GameCommandCodec.MaxCommandBytes)){w.WriteUInt16(GameCommandCodec.Version);w.WriteUInt16(435);w.WriteInt32(int.MaxValue);Reject(()=>GameCommandCodec.Decode(w.ToArray(),empty));}
        Console.WriteLine($"PASS surface batches: 4 typed schemas, {rejected} truncated/trailing payloads, {invalid.Length} invalid packed/bounded data fixtures.");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Reject(Action test){try{test();}catch(ProtocolException){return;}throw new Exception("Malformed surface batch accepted.");}
}
