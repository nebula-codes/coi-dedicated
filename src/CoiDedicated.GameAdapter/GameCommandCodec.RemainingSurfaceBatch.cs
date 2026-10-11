using System.Collections.Generic;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Surfaces;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        private const int MaxSurfaceBatchRecords=2048;
        private static bool TryEncodeRemainingSurfaceBatch(IInputCommand command,PayloadWriter writer)
        {
            var type=command.GetType();ImmutableArray<TileSurfaceCopyPasteData> data;
            if(type==typeof(BatchAddSurfaceDecalCmd)){writer.WriteUInt16(435);data=((BatchAddSurfaceDecalCmd)command).Data;}
            else if(type==typeof(BatchRemoveSurfaceDecalCmd)){writer.WriteUInt16(436);data=((BatchRemoveSurfaceDecalCmd)command).Data;}
            else if(type==typeof(BatchRemoveSurfacePlacingDesignationsCmd)){writer.WriteUInt16(437);data=((BatchRemoveSurfacePlacingDesignationsCmd)command).Data;}
            else if(type==typeof(PasteSurfaceDesignationsCmd))
            {
                var value=(PasteSurfaceDesignationsCmd)command;writer.WriteUInt16(438);ValidateProtoId(value.ProtoId.Value);writer.WriteString(value.ProtoId.Value,MaxProtoIdBytes);data=value.Data;
            }
            else return false;
            WriteSurfaceBatchData(writer,data);return true;
        }
        private static IInputCommand? DecodeRemainingSurfaceBatch(GameCommandKind kind,PayloadReader reader,DependencyResolver resolver)
        {
            int id=(ushort)kind;if(id<435 || id>438)return null;
            string? designation=id==438?ReadScalarProtoId(reader):null;
            var data=ReadSurfaceBatchData(reader);reader.EnsureEnd();
            if(designation!=null && !RequireResearchTradeProto<SurfaceDesignationProto>(resolver,designation).IsPlacing)
                throw new ProtocolException("Surface paste requires a placing designation.");
            foreach(var item in data)
            {
                RequireSurfaceAreaMap(resolver,new RectangleTerrainArea2i(item.Position,new RelTile2i(item.Width,item.Height)));
                // Validate slim IDs against the snapshot's native registry. Only the paste path can
                // construct surfaces; deleting an existing surface/decal does not require research.
                if(id==438 || item.SurfaceData.SurfaceSlimId.Value!=0)
                {
                    TerrainTileSurfaceProto? surface=null;
                    foreach(var proto in resolver.Resolve<ProtosDb>().All<TerrainTileSurfaceProto>())if(proto.SlimId==item.SurfaceData.SurfaceSlimId){surface=proto;break;}
                    if(surface==null || (id==438 && (item.SurfaceData.IsAutoPlaced || !surface.CanBePlacedByPlayer || !surface.IsUnlockedAndAvailable)))
                        throw new ProtocolException("Surface batch refers to an unavailable surface.");
                }
                if(id==435 || item.SurfaceData.DecalSlimId.Value!=0)
                {
                    TerrainTileSurfaceDecalProto? decal=null;
                    foreach(var proto in resolver.Resolve<ProtosDb>().All<TerrainTileSurfaceDecalProto>())if(proto.SlimId==item.SurfaceData.DecalSlimId){decal=proto;break;}
                    if(decal==null || (id==435 && !decal.IsUnlockedAndAvailable))throw new ProtocolException("Surface batch refers to an unavailable decal.");
                }
            }
            switch(id)
            {
                case 435:return new BatchAddSurfaceDecalCmd(data);
                case 436:return new BatchRemoveSurfaceDecalCmd(data);
                case 437:return new BatchRemoveSurfacePlacingDesignationsCmd(data);
                default:return new PasteSurfaceDesignationsCmd(new Proto.ID(designation!),data);
            }
        }
        private static void WriteSurfaceBatchData(PayloadWriter writer,ImmutableArray<TileSurfaceCopyPasteData> data)
        {
            ValidateRange(data.Length,0,MaxSurfaceBatchRecords,"Surface batch records");writer.WriteInt32(data.Length);
            int tiles=0;var positions=new HashSet<Tile2i>();
            foreach(var item in data)
            {
                ValidateSurfaceBatchItem(item.SurfaceData,item.Position,item.Width,item.Height,ref tiles,positions);
                writer.WriteInt32(item.SurfaceData.RawValue);writer.WriteInt32(item.SurfaceData.RawValueDecal);
                writer.WriteInt32(item.Position.X);writer.WriteInt32(item.Position.Y);writer.WriteUInt16(item.Width);writer.WriteUInt16(item.Height);
            }
        }
        private static ImmutableArray<TileSurfaceCopyPasteData> ReadSurfaceBatchData(PayloadReader reader)
        {
            int count=reader.ReadInt32();ValidateRange(count,0,MaxSurfaceBatchRecords,"Surface batch records");
            var data=new TileSurfaceCopyPasteData[count];int tiles=0;var positions=new HashSet<Tile2i>();
            for(int i=0;i<count;i++)
            {
                var surface=new TileSurfaceData(reader.ReadInt32(),reader.ReadInt32());var position=new Tile2i(reader.ReadInt32(),reader.ReadInt32());
                int width=reader.ReadUInt16(),height=reader.ReadUInt16();ValidateSurfaceBatchItem(surface,position,width,height,ref tiles,positions);
                data[i]=new TileSurfaceCopyPasteData(surface,position,width,height);
            }
            return new ImmutableArray<TileSurfaceCopyPasteData>(data);
        }
        private static void ValidateSurfaceBatchItem(TileSurfaceData surface,Tile2i position,int width,int height,ref int tiles,HashSet<Tile2i> positions)
        {
            ValidateSurfaceArea(position,new RelTile2i(width,height));tiles+=width*height;
            if(tiles>MaxSurfaceAreaTiles)throw new ProtocolException("Surface batch exceeds 16384 total tiles.");
            if(!positions.Add(position))throw new ProtocolException("Surface batch repeats a tile origin.");
            // TileSurfaceData has an explicitly documented scalar bit layout. Reserved bits and
            // preview-only placement flags have no valid saved command meaning.
            if((surface.RawValue&0x1c000)!=0 || (surface.RawValueDecal&unchecked((int)0xffffc000))!=0)
                throw new ProtocolException("Surface batch contains reserved or preview-only bits.");
            if(surface.SurfaceSlimId.Value>31)throw new ProtocolException("Surface ID exceeds the native designation range.");
            if(surface.SurfaceSlimId.Value==0 && surface.RawValue!=0)throw new ProtocolException("Empty surface has noncanonical data.");
            if(surface.DecalSlimId.Value==0 && surface.RawValueDecal!=0)throw new ProtocolException("Empty decal has noncanonical data.");
        }
    }
}
