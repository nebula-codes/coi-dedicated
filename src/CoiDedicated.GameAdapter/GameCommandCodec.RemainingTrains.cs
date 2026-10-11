using System;
using System.Collections.Generic;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Trains;

namespace CoiDedicated.GameAdapter
{
public static partial class GameCommandCodec
{
    // CoI 0.8.7d native train commands. Only explicit, bounded value schemas cross the wire.
    private const int MaxTrainCars = 128, MaxTrainTracks = 256, MaxTrainConditions = 256;
    private static readonly ScalarCommandSpec[] RemainingTrainCommands = {
        Scalar<AddTrainTrackPillarCmd>(350, (v,w)=>{WriteDetailEntity(w,v.EntityId);WriteBounded(w,v.BlockIdx,0,255,"Pillar block");}, (r,d)=>{
            var id=ReadEntityId(r);int block=ReadBounded(r,0,255,"Pillar block");r.EnsureEnd();
            var track=RequireEntity<ITrainTrackMayBeElevatedFriend>(d,id);
            if(block>=track.GetTransformedPillarsData(track.TrackTransform.Transform90RotFlip).Length)throw new ProtocolException("Pillar block does not exist.");
            return new AddTrainTrackPillarCmd(id,block);
        }),
        Scalar<BuildTrainCmd>(351,WriteTrainBuild,ReadTrainBuild),
        Scalar<EditPathfindingRestrictionCmd>(352,(v,w)=>{WriteDetailEntity(w,v.WaypointId);WriteBounded(w,v.RestrictionIndex,-1,255,"Restriction index");WriteTrainRestriction(w,v.Restriction);},(r,d)=>{
            var id=ReadEntityId(r);int index=ReadBounded(r,-1,255,"Restriction index");var restriction=ReadTrainRestriction(r);r.EnsureEnd();
            RequireRestrictionIndex(d,id,index);return new EditPathfindingRestrictionCmd(id,index,ResolveTrainRestriction(d,restriction));
        }),
        Scalar<QuickRepairTrainCmd>(353,(v,w)=>{WriteRemainingTrainId(w,v.TrainId);WriteOptionalProto(w,v.ProductId.Value);},(r,d)=>{
            var id=ReadRemainingTrainId(r);string product=ReadOptionalProto(r)??throw new ProtocolException("Repair product is required.");r.EnsureEnd();RequireTrain(d,id);RequireResearchTradeProto<ProductProto>(d,product);return new QuickRepairTrainCmd(id,new ProductProto.ID(product));
        }),
        Scalar<RecoverTrainCmd>(354,(v,w)=>{WriteRemainingTrainId(w,v.TrainId);w.WriteBoolean(v.ForcePause);w.WriteBoolean(v.TargetDepot.HasValue);if(v.TargetDepot.HasValue)WriteDetailEntity(w,v.TargetDepot.Value);},(r,d)=>{
            var id=ReadRemainingTrainId(r);bool pause=r.ReadBoolean();EntityId? depot=r.ReadBoolean()?ReadEntityId(r):(EntityId?)null;r.EnsureEnd();RequireTrain(d,id);if(depot.HasValue)RequireEntity<ITrainDepot>(d,depot.Value);return new RecoverTrainCmd(id,pause,depot);
        }),
        Scalar<RemoveDepartConditionCmd>(355,(v,w)=>WriteDepartureReference(w,v.ScheduleItemId,v.RootId,v.ConditionIndex,false),(r,d)=>{
            var a=ReadDepartureReference(r,false);r.EnsureEnd();RequireDepartureCondition(d,a);return new RemoveDepartConditionCmd(a.Item,a.Root,a.Index);
        }),
        Scalar<RemovePathfindingRestrictionCmd>(356,(v,w)=>{WriteDetailEntity(w,v.WaypointId);WriteBounded(w,v.RestrictionIndex,0,255,"Restriction index");},(r,d)=>{
            var id=ReadEntityId(r);int index=ReadBounded(r,0,255,"Restriction index");r.EnsureEnd();RequireRestrictionIndex(d,id,index);return new RemovePathfindingRestrictionCmd(id,index);
        }),
        Scalar<ReorderDepartConditionCmd>(357,(v,w)=>{WriteDepartureReference(w,v.ScheduleItemId,v.RootId,v.ConditionIndex,false);WriteBounded(w,v.NewConditionIndex,0,255,"New condition index");},(r,d)=>{
            var a=ReadDepartureReference(r,false);int index=ReadBounded(r,0,255,"New condition index");r.EnsureEnd();RequireDepartureCondition(d,a);RequireDepartureCondition(d,(a.Item,a.Root,index));return new ReorderDepartConditionCmd(a.Item,a.Root,a.Index,index);
        }),
        Scalar<ReplaceEntityInScheduleCmd>(358,(v,w)=>{w.WriteBoolean(v.ScheduleItemId.HasValue);if(v.ScheduleItemId.HasValue)WriteTrainSchedule(w,v.ScheduleItemId.Value);WriteDetailEntity(w,v.OldEntityId);WriteDetailEntity(w,v.NewEntityId);},(r,d)=>{
            TrainScheduleItemId? item=r.ReadBoolean()?ReadTrainSchedule(r):(TrainScheduleItemId?)null;var oldId=ReadEntityId(r);var newId=ReadEntityId(r);r.EnsureEnd();if(item.HasValue)RequireTrainScheduleRoot(d,item.Value,oldId);RequireEntity<ITrainStationBase>(d,newId);return new ReplaceEntityInScheduleCmd(item,oldId,newId);
        }),
        Scalar<ReverseTracksCmd>(359,(v,w)=>WriteRemainingTracks(w,v.TrackIds),(r,d)=>{var ids=ReadRemainingTracks(r);r.EnsureEnd();RequireRemainingTracks(d,ids);return new ReverseTracksCmd(ids);}),
        Scalar<ScrapTrainCarCmd>(360,(v,w)=>{WriteDetailEntity(w,v.DepotId);WriteOptionalProto(w,v.CarProtoId.Value);},(r,d)=>{
            var id=ReadEntityId(r);string proto=ReadOptionalProto(r)??throw new ProtocolException("Car proto is required.");r.EnsureEnd();RequireEntity<TrainDepot>(d,id);RequireResearchTradeProto<TrainCarBaseProto>(d,proto);return new ScrapTrainCarCmd(id,new DynamicEntityProto.ID(proto));
        }),
        Scalar<SetDepartConditionCombineMethodCmd>(361,(v,w)=>{WriteDepartureReference(w,v.ScheduleItemId,v.RootId,v.ConditionIndex,false);w.WriteBoolean(v.CombineAsOr);},(r,d)=>{
            var a=ReadDepartureReference(r,false);bool combine=r.ReadBoolean();r.EnsureEnd();RequireDepartureCondition(d,a,typeof(TrainScheduleDepartConditionBase));return new SetDepartConditionCombineMethodCmd(a.Item,a.Root,a.Index,combine);
        }),
        Scalar<SetTrainLineColorCmd>(362,(v,w)=>{WriteRemainingLine(w,v.TrainLineId);WriteRemainingTrainColor(w,v.Color);},(r,d)=>{var id=ReadRemainingLine(r);var color=ReadRemainingTrainColor(r);r.EnsureEnd();RequireTrainLine(d,id);return new SetTrainLineColorCmd(id,color);}),
        Scalar<SetTrainLineIcon>(363,(v,w)=>{WriteRemainingLine(w,v.TrainLineId);WriteOptionalProto(w,v.ProtoForIcon.HasValue?v.ProtoForIcon.Value.Id.Value:null);},(r,d)=>{
            var id=ReadRemainingLine(r);string? proto=ReadOptionalProto(r);r.EnsureEnd();RequireTrainLine(d,id);Option<IProtoWithIcon> icon=Option<IProtoWithIcon>.None;
            if(proto!=null){var p=RequireResearchTradeProto<Proto>(d,proto);if(!(p is IProtoWithIcon value))throw new ProtocolException("Prototype has no icon.");icon=Option<IProtoWithIcon>.Some(value);}return new SetTrainLineIcon(id,icon);
        }),
        Scalar<SetTrainLineLocoColorModeCmd>(364,(v,w)=>{WriteRemainingLine(w,v.TrainLineId);WriteBounded(w,(int)v.Mode,0,2,"Locomotive color mode");},(r,d)=>{var id=ReadRemainingLine(r);var mode=(LocoColorMode)ReadBounded(r,0,2,"Locomotive color mode");r.EnsureEnd();RequireTrainLine(d,id);return new SetTrainLineLocoColorModeCmd(id,mode);}),
        Scalar<SetTrainLineLocoCustomColorCmd>(365,(v,w)=>{WriteRemainingLine(w,v.TrainLineId);WriteRemainingTrainColor(w,v.Color);},(r,d)=>{var id=ReadRemainingLine(r);var color=ReadRemainingTrainColor(r);r.EnsureEnd();RequireTrainLine(d,id);return new SetTrainLineLocoCustomColorCmd(id,color);}),
        Scalar<SetTrainNetworkColorCmd>(366,(v,w)=>{WriteRemainingNetwork(w,v.NetworkId);WriteRemainingTrainColor(w,v.Color);},(r,d)=>{var id=ReadRemainingNetwork(r);var color=ReadRemainingTrainColor(r);r.EnsureEnd();RequireTrainNetwork(d,id);return new SetTrainNetworkColorCmd(id,color);}),
        Scalar<SetTrainNetworkLocoColorModeCmd>(367,(v,w)=>{WriteRemainingNetwork(w,v.NetworkId);WriteBounded(w,(int)v.Mode,0,2,"Locomotive color mode");},(r,d)=>{var id=ReadRemainingNetwork(r);var mode=(LocoColorMode)ReadBounded(r,0,2,"Locomotive color mode");r.EnsureEnd();RequireTrainNetwork(d,id);return new SetTrainNetworkLocoColorModeCmd(id,mode);}),
        Scalar<SetTrainNetworkLocoCustomColorCmd>(368,(v,w)=>{WriteRemainingNetwork(w,v.NetworkId);WriteRemainingTrainColor(w,v.Color);},(r,d)=>{var id=ReadRemainingNetwork(r);var color=ReadRemainingTrainColor(r);r.EnsureEnd();RequireTrainNetwork(d,id);return new SetTrainNetworkLocoCustomColorCmd(id,color);}),
        Scalar<SetTrainTrackCriticalBlockStateCmd>(369,(v,w)=>{WriteRemainingBlocks(w,v.BlockIds);w.WriteBoolean(v.IsCritical);},(r,d)=>{var ids=ReadRemainingBlocks(r);bool value=r.ReadBoolean();r.EnsureEnd();RequireRemainingBlocks(d,ids);return new SetTrainTrackCriticalBlockStateCmd(ids,value);}),
        Scalar<SetTrainTrackSuperBlockStateCmd>(370,(v,w)=>{WriteRemainingBlocks(w,v.BlockIds);w.WriteBoolean(v.IsSuperBlock);},(r,d)=>{var ids=ReadRemainingBlocks(r);bool value=r.ReadBoolean();r.EnsureEnd();RequireRemainingBlocks(d,ids);return new SetTrainTrackSuperBlockStateCmd(ids,value);}),
        Scalar<SetWagonProductFilterCmd>(371,(v,w)=>{WriteRemainingTrainId(w,v.TrainId);WriteBounded(w,v.WagonIndex,0,127,"Wagon index");WriteBounded(w,v.SubCarIndex,0,15,"Subcar index");WriteOptionalProto(w,v.ProductId?.Value);},(r,d)=>{
            var id=ReadRemainingTrainId(r);int wagon=ReadBounded(r,0,127,"Wagon index"),subcar=ReadBounded(r,0,15,"Subcar index");string? product=ReadOptionalProto(r);r.EnsureEnd();RequireTrain(d,id);var train=d.Resolve<TrainsManager>().TrainsDict[id];
            if(wagon>=train.TrainCars.Length||!(train.TrainCars[wagon] is CargoWagon car)||subcar>=car.Prototype.SubCarCount)throw new ProtocolException("Cargo wagon/subcar does not exist.");
            if(product!=null&&!car.Prototype.ProductType.Matches(RequireResearchTradeProto<ProductProto>(d,product).Type))throw new ProtocolException("Product does not match cargo wagon.");
            return new SetWagonProductFilterCmd(id,wagon,subcar,product==null?(ProductProto.ID?)null:new ProductProto.ID(product));
        }),
        Scalar<ToggleBidirectionalCommand>(372,(v,w)=>WriteRemainingTracks(w,v.TrackIds),(r,d)=>{var ids=ReadRemainingTracks(r);r.EnsureEnd();RequireRemainingTracks(d,ids);return new ToggleBidirectionalCommand(ids);}),
        Scalar<TrainContainsDepartConditionEditCmd>(373,(v,w)=>{
            WriteDepartureReference(w,v.ScheduleItemId,v.RootId,v.ConditionIndex,true);WriteRemainingNullableBool(w,v.UseInferiorToInsteadOfSuperiorTo);
            w.WriteBoolean(v.Mode.HasValue);if(v.Mode.HasValue)WriteBounded(w,(int)v.Mode.Value,0,2,"Comparison mode");w.WriteBoolean(v.Percent.HasValue);if(v.Percent.HasValue)WriteBounded(w,v.Percent.Value.RawValue,0,100000,"Contents threshold");
            WriteOptionalProto(w,v.ProductId?.Value);w.WriteBoolean(v.ProductIdSpecified);WriteRemainingNullableBool(w,v.IsSimplified);
        },(r,d)=>{
            var a=ReadDepartureReference(r,true);bool? inferior=ReadRemainingNullableBool(r);var mode=r.ReadBoolean()?(TrainContainsDepartCondition.ComparisonMode?)ReadBounded(r,0,2,"Comparison mode"):null;
            Percent? percent=r.ReadBoolean()?Percent.FromRaw(ReadBounded(r,0,100000,"Contents threshold")):(Percent?)null;string? product=ReadOptionalProto(r);bool specified=r.ReadBoolean();bool? simplified=ReadRemainingNullableBool(r);r.EnsureEnd();
            RequireDepartureCondition(d,a,typeof(TrainContainsDepartCondition));if(product!=null)RequireResearchTradeProto<ProductProto>(d,product);
            return new TrainContainsDepartConditionEditCmd(a.Item,a.Root,a.Index,inferior,mode,percent,product==null?(ProductProto.ID?)null:new ProductProto.ID(product),specified,simplified);
        }),
        Scalar<TrainDurationDepartConditionEditCmd>(374,(v,w)=>{WriteDepartureReference(w,v.ScheduleItemId,v.RootId,v.ConditionIndex,true);w.WriteBoolean(v.Delay.HasValue);if(v.Delay.HasValue)WriteBounded(w,v.Delay.Value.Ticks,1,9999*20,"Departure delay");},(r,d)=>{
            var a=ReadDepartureReference(r,true);Duration? delay=r.ReadBoolean()?Duration.FromTicks(ReadBounded(r,1,9999*20,"Departure delay")):(Duration?)null;r.EnsureEnd();RequireDepartureCondition(d,a,typeof(TrainDurationDepartCondition));return new TrainDurationDepartConditionEditCmd(a.Item,a.Root,a.Index,delay);
        }),
    };
    private static bool TryEncodeRemainingTrains(IInputCommand v,PayloadWriter w){foreach(var s in RemainingTrainCommands)if(v.GetType()==s.Type){w.WriteUInt16(s.Id);s.Write(v,w);return true;}return false;}
    private static IInputCommand? DecodeRemainingTrains(GameCommandKind k,PayloadReader r,DependencyResolver d){foreach(var s in RemainingTrainCommands)if(s.Id==(ushort)k)return s.Read(r,d);return null;}
    private static void WriteRemainingTrainId(PayloadWriter w,TrainId id)=>WriteBounded(w,id.Value,1,ushort.MaxValue,"Train ID");
    private static TrainId ReadRemainingTrainId(PayloadReader r)=>new TrainId((ushort)ReadBounded(r,1,ushort.MaxValue,"Train ID"));
    private static void WriteRemainingLine(PayloadWriter w,TrainLineId id)=>WriteBounded(w,id.Value,0,int.MaxValue,"Train line");
    private static TrainLineId ReadRemainingLine(PayloadReader r)=>new TrainLineId(ReadBounded(r,0,int.MaxValue,"Train line"));
    private static void WriteRemainingNetwork(PayloadWriter w,TrainNetworkId id)=>WriteBounded(w,id.Value,0,int.MaxValue,"Train network");
    private static TrainNetworkId ReadRemainingNetwork(PayloadReader r)=>new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"Train network"));
    private static void WriteRemainingTrainColor(PayloadWriter w,TrainLineColor c){w.WriteUInt32(c.Primary.Rgba);w.WriteUInt32(c.Secondary.Rgba);w.WriteUInt64(c.TrainColor.Raw);}
    private static TrainLineColor ReadRemainingTrainColor(PayloadReader r)=>new TrainLineColor(new ColorRgba(r.ReadUInt32()),new ColorRgba(r.ReadUInt32()),new TrainColor(r.ReadUInt64()));
    private static void WriteRemainingNullableBool(PayloadWriter w,bool? v){w.WriteBoolean(v.HasValue);if(v.HasValue)w.WriteBoolean(v.Value);}
    private static bool? ReadRemainingNullableBool(PayloadReader r)=>r.ReadBoolean()?r.ReadBoolean():(bool?)null;
    private static void WriteDepartureReference(PayloadWriter w,TrainScheduleItemId item,EntityId root,int index,bool append){WriteTrainSchedule(w,item);WriteDetailEntity(w,root);WriteBounded(w,index,append?-1:0,MaxTrainConditions-1,"Departure condition");}
    private static (TrainScheduleItemId Item,EntityId Root,int Index) ReadDepartureReference(PayloadReader r,bool append)=>(ReadTrainSchedule(r),ReadEntityId(r),ReadBounded(r,append?-1:0,MaxTrainConditions-1,"Departure condition"));
    private static void RequireDepartureCondition(DependencyResolver d,(TrainScheduleItemId Item,EntityId Root,int Index) a,Type? expected=null)
    {
        RequireTrainScheduleRoot(d,a.Item,a.Root);var item=RequireTrainLine(d,a.Item.TrainLineId).Schedule[a.Item.ScheduleIndex];
        foreach(var root in item.StationRoots)if(root.Id==a.Root){
            if(!item.DepartConditions.TryGetValue(root,out var conditions))throw new ProtocolException("Root has no departure conditions.");
            if(a.Index<0){if(conditions.Count>=MaxTrainConditions)throw new ProtocolException("Too many departure conditions.");return;}
            if(a.Index>=conditions.Count||(expected!=null&&!expected.IsInstanceOfType(conditions[a.Index])))throw new ProtocolException("Departure condition is missing or has a different type.");return;
        }
    }
    private static void RequireRestrictionIndex(DependencyResolver d,EntityId id,int index){var entity=RequireEntity<ITrainStationRootWithPathfindingRestrictions>(d,id);if(index<0?entity.PathfindingRestrictions.Count>=256:index>=entity.PathfindingRestrictions.Count)throw new ProtocolException("Restriction index does not exist or list is full.");}
    private static void WriteRemainingTracks(PayloadWriter w,ImmutableArray<EntityId> ids){WriteBounded(w,ids.Length,1,MaxTrainTracks,"Track count");var seen=new HashSet<EntityId>();foreach(var id in ids){if(!seen.Add(id))throw new ProtocolException("Duplicate track.");WriteDetailEntity(w,id);}}
    private static ImmutableArray<EntityId> ReadRemainingTracks(PayloadReader r){int count=ReadBounded(r,1,MaxTrainTracks,"Track count");var ids=new EntityId[count];var seen=new HashSet<EntityId>();for(int i=0;i<count;i++){ids[i]=ReadEntityId(r);if(!seen.Add(ids[i]))throw new ProtocolException("Duplicate track.");}return new ImmutableArray<EntityId>(ids);}
    private static void RequireRemainingTracks(DependencyResolver d,ImmutableArray<EntityId> ids){foreach(var id in ids)RequireEntity<IEntityWithTrainTrackFriend>(d,id);}
    private static void WriteRemainingBlocks(PayloadWriter w,ImmutableArray<Pair<EntityId,byte>> ids){WriteBounded(w,ids.Length,1,MaxTrainTracks,"Block count");var seen=new HashSet<Pair<EntityId,byte>>();foreach(var id in ids){if(!seen.Add(id))throw new ProtocolException("Duplicate track block.");WriteDetailEntity(w,id.First);w.WriteUInt16(id.Second);}}
    private static ImmutableArray<Pair<EntityId,byte>> ReadRemainingBlocks(PayloadReader r){int count=ReadBounded(r,1,MaxTrainTracks,"Block count");var ids=new Pair<EntityId,byte>[count];var seen=new HashSet<Pair<EntityId,byte>>();for(int i=0;i<count;i++){var id=ReadEntityId(r);int block=r.ReadUInt16();ValidateRange(block,0,255,"Block index");ids[i]=new Pair<EntityId,byte>(id,(byte)block);if(!seen.Add(ids[i]))throw new ProtocolException("Duplicate track block.");}return new ImmutableArray<Pair<EntityId,byte>>(ids);}
    private static void RequireRemainingBlocks(DependencyResolver d,ImmutableArray<Pair<EntityId,byte>> ids){foreach(var id in ids){var entity=RequireEntity<IEntityWithTrainTrack>(d,id.First);if(id.Second>=entity.TrackProto.BlocksCount)throw new ProtocolException("Track block does not exist.");}}
    private sealed class TrainRestrictionData
    {
        public int Kind, Value, Grouping, Line, Network;
        public bool Deny, Less;
        public string? Product;
    }
    private static void WriteTrainRestriction(PayloadWriter w,ITrainPathfindingRestriction value)
    {
        if(value==null)throw new ProtocolException("Restriction is required.");
        var type=value.GetType();int kind=type==typeof(TrainCarriesProductPathfindingRestriction)?1:type==typeof(TrainGroupingPathfindingRestriction)?2:type==typeof(TrainLengthPathfindingRestriction)?3:type==typeof(TrainMaxSpeedPathfindingRestriction)?4:type==typeof(TrainWagonTypePathfindingRestriction)?5:0;
        if(kind==0)throw new ProtocolException("Unsupported pathfinding restriction type.");
        w.WriteUInt16((ushort)kind);w.WriteBoolean(((TrainPathfindingRestrictionBase)value).IsDeny);
        switch(kind){
            case 1:var p=(TrainCarriesProductPathfindingRestriction)value;WriteOptionalProto(w,p.Product.HasValue?p.Product.Value.Id.Value:null);break;
            case 2:var g=(TrainGroupingPathfindingRestriction)value;WriteBounded(w,(int)g.Kind,0,1,"Grouping kind");WriteBounded(w,g.LineId.Raw,0,int.MaxValue,"Line ID");WriteBounded(w,g.NetworkId.Raw,0,int.MaxValue,"Network ID");break;
            case 3:var l=(TrainLengthPathfindingRestriction)value;w.WriteBoolean(l.UseLessThanInsteadOfGreaterEqual);WriteBounded(w,l.Threshold.Value,0,9999,"Train length");break;
            case 4:var s=(TrainMaxSpeedPathfindingRestriction)value;w.WriteBoolean(s.UseLessThanInsteadOfGreaterEqual);WriteBounded(w,s.Threshold.Value.RawValue,0,9999.Kmh().Value.RawValue,"Train speed");break;
            case 5:WriteBounded(w,(int)((TrainWagonTypePathfindingRestriction)value).Kind,0,5,"Wagon type");break;
        }
    }
    private static TrainRestrictionData ReadTrainRestriction(PayloadReader r)
    {
        var a=new TrainRestrictionData {Kind=r.ReadUInt16()};ValidateRange(a.Kind,1,5,"Restriction kind");a.Deny=r.ReadBoolean();
        switch(a.Kind){
            case 1:a.Product=ReadOptionalProto(r);break;
            case 2:a.Grouping=ReadBounded(r,0,1,"Grouping kind");a.Line=ReadBounded(r,0,int.MaxValue,"Line ID");a.Network=ReadBounded(r,0,int.MaxValue,"Network ID");break;
            case 3:a.Less=r.ReadBoolean();a.Value=ReadBounded(r,0,9999,"Train length");break;
            case 4:a.Less=r.ReadBoolean();a.Value=ReadBounded(r,0,9999.Kmh().Value.RawValue,"Train speed");break;
            case 5:a.Value=ReadBounded(r,0,5,"Wagon type");break;
        }return a;
    }
    private static ITrainPathfindingRestriction ResolveTrainRestriction(DependencyResolver d,TrainRestrictionData a)
    {
        switch(a.Kind){
            case 1:return new TrainCarriesProductPathfindingRestriction {IsDeny=a.Deny,Product=a.Product==null?Option<ProductProto>.None:RequireResearchTradeProto<ProductProto>(d,a.Product)};
            case 2:
                var line=new TrainLineIdOrNone(a.Line);var network=new TrainNetworkIdOrNone(a.Network);
                // Native grouping rules retain both selections; only the active selection participates in matching.
                if(a.Grouping==0&&line.HasValue)RequireTrainLine(d,line.Value);if(a.Grouping==1&&network.HasValue)RequireTrainNetwork(d,network.Value);
                return new TrainGroupingPathfindingRestriction {IsDeny=a.Deny,Kind=(TrainGroupingPathfindingRestriction.GroupingKind)a.Grouping,LineId=line,NetworkId=network};
            case 3:return new TrainLengthPathfindingRestriction {IsDeny=a.Deny,UseLessThanInsteadOfGreaterEqual=a.Less,Threshold=new RelTile1i(a.Value)};
            case 4:return new TrainMaxSpeedPathfindingRestriction {IsDeny=a.Deny,UseLessThanInsteadOfGreaterEqual=a.Less,Threshold=new RelTile1f(Fix32.FromRaw(a.Value))};
            case 5:return new TrainWagonTypePathfindingRestriction {IsDeny=a.Deny,Kind=(TrainWagonTypePathfindingRestriction.WagonTypeKind)a.Value};
            default:throw new ProtocolException("Unsupported pathfinding restriction.");
        }
    }
    private static void WriteTrainBuild(BuildTrainCmd v,PayloadWriter w)
    {
        WriteDetailEntity(w,v.DepotId);WriteBounded(w,v.CarDesigns.Length,1,MaxTrainCars,"Train car count");
        foreach(var car in v.CarDesigns){ValidateProtoId(car.ProtoId.Value);w.WriteString(car.ProtoId.Value,MaxProtoIdBytes);w.WriteBoolean(car.IsFlipped);WriteOptionalProto(w,car.PrimaryProduct?.Value);WriteOptionalProto(w,car.SecondaryProduct?.Value);}
        WriteBounded(w,v.TrainLineId.Raw,0,int.MaxValue,"Line ID");WriteBounded(w,v.TrainNetworkId.Raw,0,int.MaxValue,"Network ID");WriteBounded(w,v.TrainToReplace.Raw,0,ushort.MaxValue,"Replacement train");
    }
    private static BuildTrainCmd ReadTrainBuild(PayloadReader r,DependencyResolver d)
    {
        var depotId=ReadEntityId(r);int count=ReadBounded(r,1,MaxTrainCars,"Train car count");var cars=new TrainCarDesignWithId[count];
        for(int i=0;i<count;i++){
            string proto=ReadScalarProtoId(r);bool flipped=r.ReadBoolean();string? primary=ReadOptionalProto(r),secondary=ReadOptionalProto(r);
            cars[i]=new TrainCarDesignWithId(new DynamicEntityProto.ID(proto),flipped,primary==null?(ProductProto.ID?)null:new ProductProto.ID(primary),secondary==null?(ProductProto.ID?)null:new ProductProto.ID(secondary));
        }
        var line=new TrainLineIdOrNone(ReadBounded(r,0,int.MaxValue,"Line ID"));var network=new TrainNetworkIdOrNone(ReadBounded(r,0,int.MaxValue,"Network ID"));var replacement=new TrainIdOrNone((ushort)ReadBounded(r,0,ushort.MaxValue,"Replacement train"));r.EnsureEnd();
        var depot=RequireEntity<ITrainDepot>(d,depotId);if(line.HasValue)RequireTrainLine(d,line.Value);if(network.HasValue)RequireTrainNetwork(d,network.Value);if(replacement.HasValue)RequireTrain(d,replacement.Value);
        foreach(var car in cars){var proto=RequireResearchTradeProto<TrainCarBaseProto>(d,car.ProtoId.Value);ValidateTrainCarFilter(d,proto,car.PrimaryProduct,0);ValidateTrainCarFilter(d,proto,car.SecondaryProduct,1);}
        // Research, depot length, locomotive arrangement, replacement and construction costs remain native processor decisions.
        return new BuildTrainCmd(depot,new ImmutableArray<TrainCarDesignWithId>(cars),line,network,replacement);
    }
    private static void ValidateTrainCarFilter(DependencyResolver d,TrainCarBaseProto proto,ProductProto.ID? product,int subcar)
    {
        if(!product.HasValue)return;var p=RequireResearchTradeProto<ProductProto>(d,product.Value.Value);
        if(!(proto is CargoWagonProto wagon)||subcar>=proto.SubCarCount||!wagon.ProductType.Matches(p.Type))throw new ProtocolException("Car product filter is incompatible with its wagon.");
    }
}
}
