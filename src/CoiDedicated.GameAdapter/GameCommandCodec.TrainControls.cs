// Explicit schemas for public CoI 0.8.7d train commands; stable IDs, no runtime reflection.
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Core.Entities;
using Mafi.Core.Products;
using Mafi.Core.Trains;
namespace CoiDedicated.GameAdapter { public static partial class GameCommandCodec {
private static readonly ScalarCommandSpec[] TrainCommands = {
Scalar<RemoveTrainLineScheduleItemCmd>(252,(v,w)=>WriteTrainSchedule(w,v.ScheduleItem),(r,d)=>{
    var item=ReadTrainSchedule(r);r.EnsureEnd();RequireTrainSchedule(d,item);return new RemoveTrainLineScheduleItemCmd(item);
}),
Scalar<AddEntityToScheduleItemCmd>(216,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    WriteDetailEntity(w,v.EntityId);
    WriteBounded(w,v.Priority,0,14,"Priority");
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    var EntityId=ReadEntityId(r);
    int Priority=ReadBounded(r,0,14,"Priority");
    r.EnsureEnd();
    RequireTrainSchedule(d,ScheduleItemId);
    RequireEntity<IEntity>(d,EntityId);
    return new AddEntityToScheduleItemCmd(ScheduleItemId,EntityId,Priority);
}),
Scalar<AddNewScheduleItemToTrainLineCmd>(217,(v,w)=>{
    WriteBounded(w,v.TrainLineId.Value,0,int.MaxValue,"TrainLineId");
    WriteDetailEntity(w,v.EntityId);
    WriteBounded(w,v.Index,-1,4095,"Index");
    WriteBounded(w,v.Priority,0,14,"Priority");
},(r,d)=>{
    var TrainLineId=new TrainLineId(ReadBounded(r,0,int.MaxValue,"TrainLineId"));
    var EntityId=ReadEntityId(r);
    int Index=ReadBounded(r,-1,4095,"Index");
    int Priority=ReadBounded(r,0,14,"Priority");
    r.EnsureEnd();
    RequireTrainLine(d,TrainLineId);
    RequireEntity<IEntity>(d,EntityId);
    if(Index>RequireTrainLine(d,TrainLineId).Schedule.Count)throw new ProtocolException("Schedule insertion exceeds its current length.");
    return new AddNewScheduleItemToTrainLineCmd(TrainLineId,EntityId,Priority,Index);
}),
Scalar<AddRemoveTrainScheduleItemProductFilterCmd>(218,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    ValidateProtoId(v.ProductId.Value);w.WriteString(v.ProductId.Value,MaxProtoIdBytes);
    w.WriteBoolean(v.IsRemove);
    w.WriteBoolean(v.IsUnload);
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    var ProductId=new ProductProto.ID(ReadScalarProtoId(r));
    bool IsRemove=r.ReadBoolean();
    bool IsUnload=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainSchedule(d,ScheduleItemId);
    RequireResearchTradeProto<ProductProto>(d,ProductId.Value);
    return new AddRemoveTrainScheduleItemProductFilterCmd(ScheduleItemId,RequireResearchTradeProto<ProductProto>(d,ProductId.Value),IsRemove,IsUnload);
}),
Scalar<AssignTrainLineToTrainCmd>(219,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
    WriteBounded(w,v.TrainLineId.Raw,0,int.MaxValue,"TrainLineIdOrNone");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    var TrainLineId=new TrainLineIdOrNone(ReadBounded(r,0,int.MaxValue,"TrainLineIdOrNone"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    if(TrainLineId.HasValue)RequireTrainLine(d,TrainLineId.Value);
    return new AssignTrainLineToTrainCmd(TrainId,TrainLineId);
}),
Scalar<AssignTrainLineToUnfinishedTrainCmd>(220,(v,w)=>{
    WriteDetailEntity(w,v.DepotId);
    WriteBounded(w,v.QueueIndex,0,4095,"QueueIndex");
    WriteBounded(w,v.TrainLineId.Raw,0,int.MaxValue,"TrainLineIdOrNone");
    WriteBounded(w,v.TrainNetworkId.Raw,0,int.MaxValue,"TrainNetworkIdOrNone");
},(r,d)=>{
    var DepotId=ReadEntityId(r);
    int QueueIndex=ReadBounded(r,0,4095,"QueueIndex");
    var TrainLineId=new TrainLineIdOrNone(ReadBounded(r,0,int.MaxValue,"TrainLineIdOrNone"));
    var TrainNetworkId=new TrainNetworkIdOrNone(ReadBounded(r,0,int.MaxValue,"TrainNetworkIdOrNone"));
    r.EnsureEnd();
    RequireEntity<IEntity>(d,DepotId);
    if(TrainLineId.HasValue)RequireTrainLine(d,TrainLineId.Value);
    if(TrainNetworkId.HasValue)RequireTrainNetwork(d,TrainNetworkId.Value);
    return new AssignTrainLineToUnfinishedTrainCmd(DepotId,QueueIndex,TrainLineId,TrainNetworkId);
}),
Scalar<AssignTrainToNetworkCmd>(221,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
    WriteBounded(w,v.NetworkId.Raw,0,int.MaxValue,"TrainNetworkIdOrNone");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    var NetworkId=new TrainNetworkIdOrNone(ReadBounded(r,0,int.MaxValue,"TrainNetworkIdOrNone"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    if(NetworkId.HasValue)RequireTrainNetwork(d,NetworkId.Value);
    return new AssignTrainToNetworkCmd(TrainId,NetworkId);
}),
Scalar<CancelTrainBuildCmd>(222,(v,w)=>{
    WriteDetailEntity(w,v.DepotId);
    WriteBounded(w,v.QueueIndex,0,4095,"QueueIndex");
},(r,d)=>{
    var DepotId=ReadEntityId(r);
    int QueueIndex=ReadBounded(r,0,4095,"QueueIndex");
    r.EnsureEnd();
    RequireEntity<IEntity>(d,DepotId);
    return new CancelTrainBuildCmd(DepotId,QueueIndex);
}),
Scalar<CancelTrainJobCmd>(223,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    return new CancelTrainJobCmd(TrainId);
}),
Scalar<CancelTrainReplacementCmd>(224,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    return new CancelTrainReplacementCmd(TrainId);
}),
Scalar<NavigateTrainToCmd>(225,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
    WriteDetailEntity(w,v.TargetEntityId);
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    var TargetEntityId=ReadEntityId(r);
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    RequireEntity<IEntity>(d,TargetEntityId);
    return new NavigateTrainToCmd(TrainId,TargetEntityId);
}),
Scalar<RefuelTrainCmd>(226,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    return new RefuelTrainCmd(TrainId);
}),
Scalar<RemoveEntityFromScheduleItemCmd>(227,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    WriteDetailEntity(w,v.EntityId);
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    var EntityId=ReadEntityId(r);
    r.EnsureEnd();
    RequireTrainScheduleRoot(d,ScheduleItemId,EntityId);
    return new RemoveEntityFromScheduleItemCmd(ScheduleItemId,EntityId);
}),
Scalar<RemoveTrainLineCmd>(228,(v,w)=>{
    WriteBounded(w,v.TrainLineId.Value,0,int.MaxValue,"TrainLineId");
},(r,d)=>{
    var TrainLineId=new TrainLineId(ReadBounded(r,0,int.MaxValue,"TrainLineId"));
    r.EnsureEnd();
    RequireTrainLine(d,TrainLineId);
    return new RemoveTrainLineCmd(TrainLineId);
}),
Scalar<RemoveTrainNetworkCmd>(229,(v,w)=>{
    WriteBounded(w,v.NetworkId.Value,0,int.MaxValue,"TrainNetworkId");
},(r,d)=>{
    var NetworkId=new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"TrainNetworkId"));
    r.EnsureEnd();
    RequireTrainNetwork(d,NetworkId);
    return new RemoveTrainNetworkCmd(NetworkId);
}),
Scalar<RenameTrainLineCmd>(230,(v,w)=>{
    WriteBounded(w,v.TrainLineId.Value,0,int.MaxValue,"TrainLineId");
    ValidateEntityTitle(v.Name);w.WriteString(v.Name,256);
},(r,d)=>{
    var TrainLineId=new TrainLineId(ReadBounded(r,0,int.MaxValue,"TrainLineId"));
    string Name=r.ReadString(256);ValidateEntityTitle(Name);
    r.EnsureEnd();
    RequireTrainLine(d,TrainLineId);
    return new RenameTrainLineCmd(TrainLineId,Name);
}),
Scalar<RenameTrainNetworkCmd>(231,(v,w)=>{
    WriteBounded(w,v.NetworkId.Value,0,int.MaxValue,"TrainNetworkId");
    ValidateEntityTitle(v.Name);w.WriteString(v.Name,256);
},(r,d)=>{
    var NetworkId=new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"TrainNetworkId"));
    string Name=r.ReadString(256);ValidateEntityTitle(Name);
    r.EnsureEnd();
    RequireTrainNetwork(d,NetworkId);
    return new RenameTrainNetworkCmd(NetworkId,Name);
}),
Scalar<ReorderTrainLineScheduleItemCmd>(232,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItem);
    WriteBounded(w,v.NewIndex,0,4095,"NewIndex");
},(r,d)=>{
    var ScheduleItem=ReadTrainSchedule(r);
    int NewIndex=ReadBounded(r,0,4095,"NewIndex");
    r.EnsureEnd();
    RequireTrainSchedule(d,ScheduleItem);
    if(NewIndex>=RequireTrainLine(d,ScheduleItem.TrainLineId).Schedule.Count)throw new ProtocolException("Schedule destination does not exist.");
    return new ReorderTrainLineScheduleItemCmd(ScheduleItem,NewIndex);
}),
Scalar<SetArrivalRuleNetworkPriorityCmd>(233,(v,w)=>{
    WriteDetailEntity(w,v.StationId);
    ValidateProtoId(v.ProductId.Value);w.WriteString(v.ProductId.Value,MaxProtoIdBytes);
    WriteBounded(w,v.Priority,0,14,"Priority");
},(r,d)=>{
    var StationId=ReadEntityId(r);
    var ProductId=new ProductProto.ID(ReadScalarProtoId(r));
    int Priority=ReadBounded(r,0,14,"Priority");
    r.EnsureEnd();
    RequireEntity<IEntity>(d,StationId);
    RequireResearchTradeProto<ProductProto>(d,ProductId.Value);
    return new SetArrivalRuleNetworkPriorityCmd(StationId,ProductId,Priority);
}),
Scalar<SetArrivalRuleNetworkThresholdCmd>(234,(v,w)=>{
    WriteDetailEntity(w,v.StationId);
    ValidateProtoId(v.ProductId.Value);w.WriteString(v.ProductId.Value,MaxProtoIdBytes);
    WriteBounded(w,v.Threshold.RawValue,0,100000,"Threshold");
},(r,d)=>{
    var StationId=ReadEntityId(r);
    var ProductId=new ProductProto.ID(ReadScalarProtoId(r));
    var Threshold=Percent.FromRaw(ReadBounded(r,0,100000,"Threshold"));
    r.EnsureEnd();
    RequireEntity<IEntity>(d,StationId);
    RequireResearchTradeProto<ProductProto>(d,ProductId.Value);
    return new SetArrivalRuleNetworkThresholdCmd(StationId,ProductId,Threshold);
}),
Scalar<SetNetworkCargoStationCmd>(235,(v,w)=>{
    WriteBounded(w,v.NetworkId.Value,0,int.MaxValue,"TrainNetworkId");
    WriteDetailEntity(w,v.StationId);
    w.WriteBoolean(v.Add);
},(r,d)=>{
    var NetworkId=new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"TrainNetworkId"));
    var StationId=ReadEntityId(r);
    bool Add=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainNetwork(d,NetworkId);
    RequireEntity<IEntity>(d,StationId);
    return new SetNetworkCargoStationCmd(NetworkId,StationId,Add);
}),
Scalar<SetNetworkFuelStationCmd>(236,(v,w)=>{
    WriteBounded(w,v.NetworkId.Value,0,int.MaxValue,"TrainNetworkId");
    WriteDetailEntity(w,v.StationId);
    w.WriteBoolean(v.Add);
},(r,d)=>{
    var NetworkId=new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"TrainNetworkId"));
    var StationId=ReadEntityId(r);
    bool Add=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainNetwork(d,NetworkId);
    RequireEntity<IEntity>(d,StationId);
    return new SetNetworkFuelStationCmd(NetworkId,StationId,Add);
}),
Scalar<SetNetworkRefuelThresholdCmd>(237,(v,w)=>{
    WriteBounded(w,v.NetworkId.Value,0,int.MaxValue,"TrainNetworkId");
    WriteBounded(w,v.Threshold.RawValue,0,100000,"Threshold");
},(r,d)=>{
    var NetworkId=new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"TrainNetworkId"));
    var Threshold=Percent.FromRaw(ReadBounded(r,0,100000,"Threshold"));
    r.EnsureEnd();
    RequireTrainNetwork(d,NetworkId);
    return new SetNetworkRefuelThresholdCmd(NetworkId,Threshold);
}),
Scalar<SetNetworkWaitingBayCmd>(238,(v,w)=>{
    WriteBounded(w,v.NetworkId.Value,0,int.MaxValue,"TrainNetworkId");
    WriteDetailEntity(w,v.StationId);
    w.WriteBoolean(v.Add);
},(r,d)=>{
    var NetworkId=new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"TrainNetworkId"));
    var StationId=ReadEntityId(r);
    bool Add=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainNetwork(d,NetworkId);
    RequireEntity<IEntity>(d,StationId);
    return new SetNetworkWaitingBayCmd(NetworkId,StationId,Add);
}),
Scalar<SetTrainLineColorApplicationCmd>(239,(v,w)=>{
    WriteBounded(w,v.TrainLineId.Value,0,int.MaxValue,"TrainLineId");
    w.WriteBoolean(v.ApplyLineColorToTrains);
},(r,d)=>{
    var TrainLineId=new TrainLineId(ReadBounded(r,0,int.MaxValue,"TrainLineId"));
    bool ApplyLineColorToTrains=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainLine(d,TrainLineId);
    return new SetTrainLineColorApplicationCmd(TrainLineId,ApplyLineColorToTrains);
}),
Scalar<SetTrainNetworkColorApplicationCmd>(240,(v,w)=>{
    WriteBounded(w,v.NetworkId.Value,0,int.MaxValue,"TrainNetworkId");
    w.WriteBoolean(v.ApplyLineColorToTrains);
},(r,d)=>{
    var NetworkId=new TrainNetworkId(ReadBounded(r,0,int.MaxValue,"TrainNetworkId"));
    bool ApplyLineColorToTrains=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainNetwork(d,NetworkId);
    return new SetTrainNetworkColorApplicationCmd(NetworkId,ApplyLineColorToTrains);
}),
Scalar<SetTrainSchedulePriority>(241,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    WriteBounded(w,v.Priority,0,14,"Priority");
    WriteDetailEntity(w,v.RootId);
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    int Priority=ReadBounded(r,0,14,"Priority");
    var RootId=ReadEntityId(r);
    r.EnsureEnd();
    RequireTrainScheduleRoot(d,ScheduleItemId,RootId);
    return new SetTrainSchedulePriority(ScheduleItemId,Priority,RootId);
}),
Scalar<SetTrainScheduleSkipIfCannotArriveCmd>(242,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    w.WriteBoolean(v.SkipIfCannotArrive);
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    bool SkipIfCannotArrive=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainSchedule(d,ScheduleItemId);
    return new SetTrainScheduleSkipIfCannotArriveCmd(ScheduleItemId,SkipIfCannotArrive);
}),
Scalar<SetTrainScheduleSkipIfHighFuelCmd>(243,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    WriteBounded(w,v.SkipIfFuelHigherThan.RawValue,0,100000,"SkipIfFuelHigherThan");
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    var SkipIfFuelHigherThan=Percent.FromRaw(ReadBounded(r,0,100000,"SkipIfFuelHigherThan"));
    r.EnsureEnd();
    RequireTrainSchedule(d,ScheduleItemId);
    return new SetTrainScheduleSkipIfHighFuelCmd(ScheduleItemId,SkipIfFuelHigherThan);
}),
Scalar<SetTrainScheduleSkipIfNextCanArriveCmd>(244,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    w.WriteBoolean(v.SkipIfNextCanArrive);
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    bool SkipIfNextCanArrive=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainSchedule(d,ScheduleItemId);
    return new SetTrainScheduleSkipIfNextCanArriveCmd(ScheduleItemId,SkipIfNextCanArrive);
}),
Scalar<SetTrainStationTrainLimitCmd>(245,(v,w)=>{
    WriteDetailEntity(w,v.StationId);
    WriteBounded(w,v.TrainLimit,-1,4095,"TrainLimit");
},(r,d)=>{
    var StationId=ReadEntityId(r);
    int TrainLimit=ReadBounded(r,-1,4095,"TrainLimit");
    r.EnsureEnd();
    RequireEntity<IEntity>(d,StationId);
    return new SetTrainStationTrainLimitCmd(StationId,TrainLimit);
}),
Scalar<SkipScheduleItemCmd>(246,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    return new SkipScheduleItemCmd(TrainId);
}),
Scalar<ToggleLoadUnloadTrainScheduleItemCmd>(247,(v,w)=>{
    WriteTrainSchedule(w,v.ScheduleItemId);
    w.WriteBoolean(v.IsUnload);
    w.WriteBoolean(v.Disable);
},(r,d)=>{
    var ScheduleItemId=ReadTrainSchedule(r);
    bool IsUnload=r.ReadBoolean();
    bool Disable=r.ReadBoolean();
    r.EnsureEnd();
    RequireTrainSchedule(d,ScheduleItemId);
    return new ToggleLoadUnloadTrainScheduleItemCmd(ScheduleItemId,IsUnload,Disable);
}),
Scalar<ToggleScrapTrainAtNearestDepotCmd>(248,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    return new ToggleScrapTrainAtNearestDepotCmd(TrainId);
}),
Scalar<ToggleTrainPausedCmd>(249,(v,w)=>{
    WriteBounded(w,v.TrainId.Value,0,int.MaxValue,"TrainId");
},(r,d)=>{
    var TrainId=new TrainId((ushort)ReadBounded(r,0,ushort.MaxValue,"TrainId"));
    r.EnsureEnd();
    RequireTrain(d,TrainId);
    return new ToggleTrainPausedCmd(TrainId);
}),
Scalar<ToggleTrainStationModuleLoadUnloadCmd>(250,(v,w)=>{
    WriteDetailEntity(w,v.EntityId);
    w.WriteBoolean(v.Load.HasValue);if(v.Load.HasValue)w.WriteBoolean(v.Load.Value);
},(r,d)=>{
    var EntityId=ReadEntityId(r);
    bool? Load=r.ReadBoolean()?r.ReadBoolean():(bool?)null;
    r.EnsureEnd();
    RequireEntity<IEntity>(d,EntityId);
    return new ToggleTrainStationModuleLoadUnloadCmd(EntityId,Load);
}),
Scalar<UpdateTrainStationArrivalRuleCmd>(251,(v,w)=>{
    WriteDetailEntity(w,v.StationId);
    ValidateProtoId(v.ProductId.Value);w.WriteString(v.ProductId.Value,MaxProtoIdBytes);
    WriteBounded(w,v.Threshold.RawValue,0,100000,"Threshold");
},(r,d)=>{
    var StationId=ReadEntityId(r);
    var ProductId=new ProductProto.ID(ReadScalarProtoId(r));
    var Threshold=Percent.FromRaw(ReadBounded(r,0,100000,"Threshold"));
    r.EnsureEnd();
    RequireEntity<IEntity>(d,StationId);
    RequireResearchTradeProto<ProductProto>(d,ProductId.Value);
    return new UpdateTrainStationArrivalRuleCmd(StationId,ProductId,Threshold);
}),
};
private static bool TryEncodeTrainControls(IInputCommand v,PayloadWriter w){foreach(var s in TrainCommands)if(v.GetType()==s.Type){w.WriteUInt16(s.Id);s.Write(v,w);return true;}return false;}
private static IInputCommand? DecodeTrainControls(GameCommandKind k,PayloadReader r,DependencyResolver d){foreach(var s in TrainCommands)if(s.Id==(ushort)k)return s.Read(r,d);return null;}
private static TrainLine RequireTrainLine(DependencyResolver d,TrainLineId id){if(!d.Resolve<TrainLinesManager>().TryGetTrainLine(id,out var line))throw new ProtocolException("Train line does not exist.");return line;}
private static void RequireTrainNetwork(DependencyResolver d,TrainNetworkId id){if(!d.Resolve<TrainNetworksManager>().Networks.ContainsKey(id))throw new ProtocolException("Train network does not exist.");}
private static void RequireTrain(DependencyResolver d,TrainId id){if(!d.Resolve<TrainsManager>().TrainsDict.ContainsKey(id))throw new ProtocolException("Train does not exist.");}
private static void WriteTrainSchedule(PayloadWriter w,TrainScheduleItemId item){WriteBounded(w,item.TrainLineId.Value,0,int.MaxValue,"Train line ID");WriteBounded(w,item.ScheduleIndex,0,4095,"Schedule index");}
private static TrainScheduleItemId ReadTrainSchedule(PayloadReader r){int id=ReadBounded(r,0,int.MaxValue,"Train line ID"),index=ReadBounded(r,0,4095,"Schedule index");return new TrainScheduleItemId(new TrainLineId(id),index);}
private static void RequireTrainSchedule(DependencyResolver d,TrainScheduleItemId item){if(item.ScheduleIndex>=RequireTrainLine(d,item.TrainLineId).Schedule.Count)throw new ProtocolException("Schedule item does not exist.");}
private static void RequireTrainScheduleRoot(DependencyResolver d,TrainScheduleItemId item,EntityId rootId)
{
    RequireTrainSchedule(d,item);
    foreach(var root in RequireTrainLine(d,item.TrainLineId).Schedule[item.ScheduleIndex].StationRoots)
        if(root.Id==rootId)return;
    throw new ProtocolException("Station root does not belong to the selected schedule item.");
}
}}
