using System;
using System.Collections.Generic;
using System.Linq;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.SpaceProgram;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.Zippers;
using Mafi.Core.Game;
using Mafi.Core.Input;
using Mafi.Core.MessageNotifications;
using Mafi.Core.Notifications;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.SpaceProgram;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Trees;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        private const int MaxMiscSelection = 1024;
        private static readonly ScalarCommandSpec[] RemainingMiscCommands = {
            Scalar<StorageCheatClearProductCmd>(410, (v,w) => WriteDetailEntity(w,v.StorageId), (r,d) => {
                var id=ReadEntityId(r);r.EnsureEnd();RequireMiscSandbox(d);RequireEntity<Storage>(d,id);return new StorageCheatClearProductCmd(id);
            }),
            Scalar<StorageSetCheatModeCmd>(411, (v,w) => { WriteDetailEntity(w,v.StorageId);WriteBounded(w,(int)v.CheatMode,0,2,"Storage cheat mode"); }, (r,d) => {
                var id=ReadEntityId(r);var mode=(Storage.StorageCheatMode)ReadBounded(r,0,2,"Storage cheat mode");r.EnsureEnd();RequireMiscSandbox(d);RequireEntity<Storage>(d,id);return new StorageSetCheatModeCmd(id,mode);
            }),
            Scalar<SetDumpHeightOffsetCmd>(412, (v,w) => {WriteDetailEntity(w,v.EntityId);WriteBounded(w,v.Depth.Value,-4096,4095,"Dump height");}, (r,d) => {
                var id=ReadEntityId(r);int depth=ReadBounded(r,-4096,4095,"Dump height");r.EnsureEnd();RequireEntity<IEntityWithOutputToTerrain>(d,id);return new SetDumpHeightOffsetCmd(id,new ThicknessTilesI(depth));
            }),
            Scalar<ReplaceEntityCmd>(413, WriteMiscReplacement, ReadMiscReplacement),
            Scalar<CutEntitiesForPasteCmd>(414, (v,w) => {
                ValidateMiscCutSelection(v.EntitiesToCut.Length,v.TransportCutsToPerform.Length);
                WriteMiscEntities(w,v.EntitiesToCut);WriteBounded(w,v.TransportCutsToPerform.Length,0,MaxMiscSelection,"Transport cuts");
                foreach(var cut in v.TransportCutsToPerform){WriteMiscPoint3(w,cut.First);WriteMiscPoint3(w,cut.Last);}
            }, (r,d) => {
                var ids=ReadMiscEntities(r);int n=ReadBounded(r,0,MaxMiscSelection,"Transport cuts");var cuts=new ImmutableArrayBuilder<TransportCutRange>(n);
                for(int i=0;i<n;i++)cuts[i]=new TransportCutRange(ReadMiscPoint3(r,d),ReadMiscPoint3(r,d));r.EnsureEnd();
                ValidateMiscCutSelection(ids.Length,n);RequireMiscCutCanBePasted(d,ids);
                return new CutEntitiesForPasteCmd(ids,cuts.GetImmutableArrayAndClear());
            }),
            Scalar<FinishBuildOfStaticEntityCmd>(415, (v,w) => {WriteDetailEntity(w,v.EntityId);w.WriteBoolean(v.PayWithUnity);}, (r,d) => {
                var id=ReadEntityId(r);bool pay=r.ReadBoolean();r.EnsureEnd();if(!pay)RequireMiscSandbox(d);RequireEntity<IStaticEntity>(d,id);return new FinishBuildOfStaticEntityCmd(id,pay);
            }),
            Scalar<SpendUpointsOnEntitiesCmd>(416, (v,w) => {WriteMiscEntities(w,v.EntityIds);w.WriteBoolean(v.Area.HasValue);if(v.Area.HasValue)WriteSurfaceArea(w,v.Area.Value);}, (r,d) => {
                var ids=ReadMiscEntities(r);RectangleTerrainArea2i? area=r.ReadBoolean()?ReadSurfaceArea(r):null;r.EnsureEnd();
                foreach(var id in ids)RequireEntity<IStaticEntity>(d,id);if(area.HasValue)RequireSurfaceAreaMap(d,area.Value);
                return new SpendUpointsOnEntitiesCmd(ids,area);
            }),
            Scalar<StartDeconstructionOfTransportSubSectionsCmd>(417, (v,w) => {
                if(v.RemoveReason!=EntityRemoveReason.Remove)throw new ProtocolException("Only player-requested transport removal is supported.");
                WriteDetailEntity(w,v.TransportId);WriteBounded(w,v.RemovedSubSections.Length,1,MaxMiscSelection,"Transport sections");
                foreach(var p in v.RemovedSubSections){WriteMiscPoint3(w,p.First);WriteMiscPoint3(w,p.Second);}w.WriteBoolean(v.QuickRemoveWithUnity);
            }, (r,d) => {
                var id=ReadEntityId(r);int n=ReadBounded(r,1,MaxMiscSelection,"Transport sections");var items=new ImmutableArrayBuilder<Pair<Tile3i,Tile3i>>(n);
                for(int i=0;i<n;i++)items[i]=new Pair<Tile3i,Tile3i>(ReadMiscPoint3(r,d),ReadMiscPoint3(r,d));bool quick=r.ReadBoolean();r.EnsureEnd();RequireEntity<Transport>(d,id);
                var sections=items.GetImmutableArrayAndClear();foreach(var section in sections){RequireMapTile(d,section.First.Xy);RequireMapTile(d,section.Second.Xy);}return new StartDeconstructionOfTransportSubSectionsCmd(id,sections,EntityRemoveReason.Remove,quick);
            }),
            Scalar<RecoverOceanAccessCmd>(418, (v,w) => {WriteDetailEntity(w,v.EntityWithOceanAreas);WriteBounded(w,v.MaxTilesToRecover,1,MaxSurfaceAreaTiles,"Ocean recovery tiles");}, (r,d) => {
                var id=ReadEntityId(r);int n=ReadBounded(r,1,MaxSurfaceAreaTiles,"Ocean recovery tiles");r.EnsureEnd();RequireEntity<IStaticEntityWithReservedOceanV2>(d,id);return new RecoverOceanAccessCmd(id,n);
            }),
            Scalar<ToggleEnabledGroupCmd>(419, (v,w) => {WriteMiscEntities(w,v.EntityIds);w.WriteBoolean(v.PauseOnly);}, (r,d) => {
                var ids=ReadMiscEntities(r);bool pause=r.ReadBoolean();r.EnsureEnd();foreach(var id in ids)RequireEntity<IStaticEntity>(d,id);return new ToggleEnabledGroupCmd(ids,pause);
            }),
            Scalar<AddTransportPillarCmd>(420, (v,w) => {WriteDetailEntity(w,v.TransportId);WriteBounded(w,v.SupportTileIndex,0,16383,"Transport support index");}, (r,d) => {
                var id=ReadEntityId(r);int index=ReadBounded(r,0,16383,"Transport support index");r.EnsureEnd();var e=RequireEntity<Transport>(d,id);
                if(index>=e.Trajectory.TilesSupportInfo.Length)throw new ProtocolException("Transport support index does not exist.");return new AddTransportPillarCmd(id,index);
            }),
            Scalar<DeconstructTransportSegmentCmd>(421, (v,w) => {WriteDetailEntity(w,v.TransportId);WriteMiscPoint3(w,v.StartPosition);WriteMiscPoint3(w,v.EndPosition);w.WriteBoolean(v.QuickRemove);}, (r,d) => {
                var id=ReadEntityId(r);var start=ReadMiscPoint3(r,d);var end=ReadMiscPoint3(r,d);bool quick=r.ReadBoolean();r.EnsureEnd();RequireEntity<Transport>(d,id);RequireMapTile(d,start.Xy);RequireMapTile(d,end.Xy);return new DeconstructTransportSegmentCmd(id,start,end,quick);
            }),
            Scalar<StackerTowerSelectProductForSlopePreviewCmd>(422, (v,w) => {WriteDetailEntity(w,v.EntityId);WriteOptionalProto(w,v.Product.ValueOrNull?.Id.Value);}, (r,d) => {
                var id=ReadEntityId(r);string? product=ReadOptionalProto(r);r.EnsureEnd();RequireEntity<StackerTower>(d,id);return new StackerTowerSelectProductForSlopePreviewCmd(id,MiscDumpProduct(d,product));
            }),
            Scalar<StackerTowerSetDumpRadiusCmd>(423, (v,w) => {WriteDetailEntity(w,v.EntityId);WriteMiscRadius(w,v.RadiusLeft);WriteMiscRadius(w,v.RadiusRight);}, (r,d) => {
                var id=ReadEntityId(r);var left=ReadMiscRadius(r);var right=ReadMiscRadius(r);r.EnsureEnd();var e=RequireEntity<StackerTower>(d,id);
                if(left.HasValue&&left.Value>e.Prototype.MaxDumpRadius||right.HasValue&&right.Value>e.Prototype.MaxDumpRadius)throw new ProtocolException("Dump radius exceeds this tower's reach.");return new StackerTowerSetDumpRadiusCmd(id,left,right);
            }),
            Scalar<StackerTowerSetMaxSegmentsCmd>(424, (v,w) => {WriteDetailEntity(w,v.EntityId);WriteBounded(w,v.SegmentsCount,0,200,"Stacker segments");}, (r,d) => {
                var id=ReadEntityId(r);int n=ReadBounded(r,0,200,"Stacker segments");r.EnsureEnd();RequireEntity<StackerTower>(d,id);return new StackerTowerSetMaxSegmentsCmd(id,n);
            }),
            Scalar<ZipperSetPriorityPortsCmd>(425, (v,w) => {WriteDetailEntity(w,v.ZipperId);w.WriteUInt16(v.PortName);w.WriteBoolean(v.IsPrioritized.HasValue);if(v.IsPrioritized.HasValue)w.WriteBoolean(v.IsPrioritized.Value);}, (r,d) => {
                var id=ReadEntityId(r);char port=(char)r.ReadUInt16();bool? prioritized=r.ReadBoolean()?r.ReadBoolean():null;r.EnsureEnd();RequireEntity<Zipper>(d,id);return new ZipperSetPriorityPortsCmd(id,port,prioritized);
            }),
            Scalar<ChangeGameDifficultyCmd>(426, (v,w) => WriteMiscDifficulty(w,v.NewDifficulty), (r,d) => new ChangeGameDifficultyCmd(ReadMiscDifficulty(r,d))),
            Scalar<MessageNotificationDismissCmd>(427, (v,w) => WriteBounded(w,v.MessageId.Value,1,int.MaxValue,"Message ID"), (r,d) => {
                var id=new MessageNotificationId(ReadBounded(r,1,int.MaxValue,"Message ID"));r.EnsureEnd();return new MessageNotificationDismissCmd(id);
            }),
            Scalar<NotificationDismissCmd>(428, (v,w) => {
                WriteBounded(w,v.NotificationsIds.Length,1,MaxMiscSelection,"Notifications");var ids=new HashSet<uint>();foreach(var id in v.NotificationsIds){if(id.Value==0||!ids.Add(id.Value))throw new ProtocolException("Invalid or duplicate notification ID.");w.WriteUInt32(id.Value);}
            }, (r,d) => {
                int n=ReadBounded(r,1,MaxMiscSelection,"Notifications");var items=new ImmutableArrayBuilder<NotificationId>(n);var ids=new HashSet<uint>();
                for(int i=0;i<n;i++){uint id=r.ReadUInt32();if(id==0||!ids.Add(id))throw new ProtocolException("Invalid or duplicate notification ID.");items[i]=new NotificationId(id);}r.EnsureEnd();return new NotificationDismissCmd(items.GetImmutableArrayAndClear());
            }),
            Scalar<PinnedProductReorderCmd>(429, (v,w) => {WriteMiscProto(w,v.ProductToMove.Value);WriteBounded(w,v.NewIndex,0,4096,"Pinned product index");}, (r,d) => {
                string id=ReadScalarProtoId(r);int index=ReadBounded(r,0,4096,"Pinned product index");r.EnsureEnd();var product=RequireResearchTradeProto<ProductProto>(d,id);var manager=d.Resolve<PinnedProductsManager>();
                if(!manager.IsPinned(product)||index>manager.AllPinnedProducts.Count)throw new ProtocolException("Pinned product or destination index no longer exists.");return new PinnedProductReorderCmd(product.Id,index);
            }),
            Scalar<BringAsteroidToOrbitCmd>(430, (v,w) => WriteBounded(w,v.AsteroidId.Value,0,int.MaxValue,"Asteroid ID"), (r,d) => {var id=ReadMiscAsteroid(r);r.EnsureEnd();RequireMiscAsteroid(d,id);return new BringAsteroidToOrbitCmd(id);}),
            Scalar<DiscardAsteroidCmd>(431, (v,w) => WriteBounded(w,v.AsteroidId.Value,0,int.MaxValue,"Asteroid ID"), (r,d) => {var id=ReadMiscAsteroid(r);r.EnsureEnd();RequireMiscAsteroid(d,id);return new DiscardAsteroidCmd(id);}),
            Scalar<DropAsteroidAtCmd>(432, (v,w) => {WriteBounded(w,v.AsteroidId.Value,0,int.MaxValue,"Asteroid ID");WriteMiscPoint2(w,v.Position);}, (r,d) => {var id=ReadMiscAsteroid(r);var p=ReadMiscPoint2(r,d);r.EnsureEnd();RequireMiscAsteroid(d,id);RequireMapTile(d,p);return new DropAsteroidAtCmd(id,p);}),
            Scalar<ReorderLaunchPadCargoBufferCmd>(433, (v,w) => {WriteDetailEntity(w,v.LaunchPadId);WriteBounded(w,v.OldIndex,0,4096,"Cargo index");WriteBounded(w,v.NewIndexAfterRemove,0,4096,"Cargo index");}, (r,d) => {
                var id=ReadEntityId(r);int a=ReadBounded(r,0,4096,"Cargo index"),b=ReadBounded(r,0,4096,"Cargo index");r.EnsureEnd();var e=RequireEntity<RocketLaunchPad>(d,id);
                if(a>=e.CargoBuffers.Count||b>=e.CargoBuffers.Count)throw new ProtocolException("Launch pad cargo index no longer exists.");return new ReorderLaunchPadCargoBufferCmd(id,a,b);
            }),
            Scalar<SetOrbitCargoLimitCmd>(434, (v,w) => {WriteMiscProto(w,v.ProductId.Value);WriteBounded(w,v.Limit.Value,0,100000000,"Orbit cargo limit");}, (r,d) => {
                string id=ReadScalarProtoId(r);int limit=ReadBounded(r,0,100000000,"Orbit cargo limit");r.EnsureEnd();var product=RequireResearchTradeProto<ProductProto>(d,id);return new SetOrbitCargoLimitCmd(product.Id,new Quantity(limit));
            }),
            Scalar<QuickFulfillDesignationsCmd>(439, (v,w) => {WriteSurfaceArea(w,v.Area);WriteOptionalProto(w,v.Product.ValueOrNull?.Id.Value);}, (r,d) => {
                var area=ReadSurfaceArea(r);string? product=ReadOptionalProto(r);r.EnsureEnd();RequireMiscSandbox(d);RequireSurfaceAreaMap(d,area);return new QuickFulfillDesignationsCmd(area,MiscDumpProduct(d,product));
            }),
            Scalar<QuickRemovePropsCmd>(440, (v,w) => WriteSurfaceArea(w,v.Area), (r,d) => {var area=ReadSurfaceArea(r);r.EnsureEnd();RequireSurfaceAreaMap(d,area);return new QuickRemovePropsCmd(area);}),
            Scalar<RemoveManualPlantTreeCmd>(441, (v,w) => WriteMiscPoint2(w,new Tile2i(v.Id.Position.X,v.Id.Position.Y)), (r,d) => {var p=ReadMiscPoint2(r,d);r.EnsureEnd();RequireMapTile(d,p);return new RemoveManualPlantTreeCmd(new TreeId(new Tile2iSlim((ushort)p.X,(ushort)p.Y)));})
        };

        private static bool TryEncodeRemainingMisc(IInputCommand command,PayloadWriter writer)
        {foreach(var spec in RemainingMiscCommands)if(command.GetType()==spec.Type){writer.WriteUInt16(spec.Id);spec.Write(command,writer);return true;}return false;}
        private static IInputCommand? DecodeRemainingMisc(GameCommandKind kind,PayloadReader reader,DependencyResolver resolver)
        {foreach(var spec in RemainingMiscCommands)if((ushort)kind==spec.Id)return spec.Read(reader,resolver);return null;}
        private static void RequireMiscSandbox(DependencyResolver d)
        {if(!d.Resolve<SandboxManager>().CanCheat)throw new ProtocolException("This action requires the island's sandbox mode.");}
        private static void WriteMiscProto(PayloadWriter w,string id){ValidateProtoId(id);w.WriteString(id,MaxProtoIdBytes);}
        private static void WriteMiscPoint2(PayloadWriter w,Tile2i p){ValidateTile(p);w.WriteInt32(p.X);w.WriteInt32(p.Y);}
        private static Tile2i ReadMiscPoint2(PayloadReader r,DependencyResolver d){var p=new Tile2i(r.ReadInt32(),r.ReadInt32());ValidateTile(p);return p;}
        private static void WriteMiscPoint3(PayloadWriter w,Tile3i p){ValidatePlacementScalars(p,0);w.WriteInt32(p.X);w.WriteInt32(p.Y);w.WriteInt32(p.Z);}
        private static Tile3i ReadMiscPoint3(PayloadReader r,DependencyResolver d){var p=new Tile3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());ValidatePlacementScalars(p,0);return p;}
        private static void WriteMiscEntities(PayloadWriter w,ImmutableArray<EntityId> items)
        {WriteBounded(w,items.Length,0,MaxMiscSelection,"Selected entities");var seen=new HashSet<int>();foreach(var id in items){if(!seen.Add(id.Value))throw new ProtocolException("Duplicate selected entity.");WriteDetailEntity(w,id);}}
        private static ImmutableArray<EntityId> ReadMiscEntities(PayloadReader r)
        {int n=ReadBounded(r,0,MaxMiscSelection,"Selected entities");var items=new ImmutableArrayBuilder<EntityId>(n);var seen=new HashSet<int>();for(int i=0;i<n;i++){var id=ReadEntityId(r);if(!seen.Add(id.Value))throw new ProtocolException("Duplicate selected entity.");items[i]=id;}return items.GetImmutableArrayAndClear();}
        private static void ValidateMiscCutSelection(int entityCount,int transportCount)
        {
            if(transportCount!=0)throw new ProtocolException("Cutting transports is unavailable until their copied configuration can be pasted in multiplayer.");
            if(entityCount<1||entityCount>MaxBuildingsPerBatch)throw new ProtocolException($"Cut selections must contain 1 to {MaxBuildingsPerBatch} supported buildings so they can be pasted together.");
        }
        private static void RequireMiscCutCanBePasted(DependencyResolver d,ImmutableArray<EntityId> ids)
        {
            // Native cut destroys the originals before its result reaches the paste tool.
            // Check the actual native source configurations against the entire paste schema,
            // including unknown settings, prototype references, count and byte limits, first.
            // Admission isolates this command so earlier commands cannot change those settings
            // between this preflight and native execution. No temporary command is scheduled.
            var helper=d.Resolve<EntitiesCloneConfigHelper>();
            var configs=new ImmutableArrayBuilder<EntityConfigData>(ids.Length);
            for(int i=0;i<ids.Length;i++)configs[i]=helper.CreateConfigFrom(RequireEntity<IStaticEntity>(d,ids[i]));
            var paste=new BatchCreateStaticEntitiesCmd(configs.GetImmutableArrayAndClear(),BuildMiniZippersMode.DeferToProto,
                isFree:false,allowValidationSuppression:false,applyConfiguration:true);
            if(!TryEncode(paste,out var payload,out string reason))throw new ProtocolException("Cannot cut a selection that multiplayer cannot paste: "+reason);
            Decode(payload,d);
        }
        private static void WriteMiscRadius(PayloadWriter w,RelTile1i? v){w.WriteBoolean(v.HasValue);if(v.HasValue)WriteBounded(w,v.Value.Value,0,16384,"Dump radius");}
        private static RelTile1i? ReadMiscRadius(PayloadReader r)=>r.ReadBoolean()?new RelTile1i(ReadBounded(r,0,16384,"Dump radius")):(RelTile1i?)null;
        private static Option<ProductProto> MiscDumpProduct(DependencyResolver d,string? id)
        {if(id==null)return Option<ProductProto>.None;var p=RequireResearchTradeProto<ProductProto>(d,id);if(p.DumpableProduct.IsNone)throw new ProtocolException("Product cannot be dumped into terrain.");return p;}
        private static AsteroidId ReadMiscAsteroid(PayloadReader r)=>new AsteroidId(ReadBounded(r,0,int.MaxValue,"Asteroid ID"));
        private static void RequireMiscAsteroid(DependencyResolver d,AsteroidId id)
        {if(!d.Resolve<AsteroidsManager>().AsteroidsActive.Any(x=>x.Id==id))throw new ProtocolException("Asteroid no longer exists.");}

        // Only native enumerated difficulty choices travel on the wire. The network cannot name
        // a property, instantiate a CLR type, or deserialize a game configuration graph.
        private static Array MiscDifficultyChoices(IDiffSettingInfo setting)
        {return setting.GetType().GetField("Options")?.GetValue(setting) as Array??throw new ProtocolException("Game difficulty option layout changed.");}
        private static void WriteMiscDifficulty(PayloadWriter w,GameDifficultyConfig config)
        {
            if(GameDifficultyConfig.AllOptions.Length!=34)throw new ProtocolException("Game difficulty option layout changed.");
            foreach(var setting in GameDifficultyConfig.AllOptions){var options=MiscDifficultyChoices(setting);object value=setting.Property.GetValue(config)!;int index=-1;
                for(int i=0;i<options.Length;i++)if(Equals(options.GetValue(i),value)){index=i;break;}
                if(index<0||index>255)throw new ProtocolException("Unsupported difficulty setting.");w.WriteUInt16((ushort)index);}
        }
        private static GameDifficultyConfig ReadMiscDifficulty(PayloadReader r,DependencyResolver d)
        {
            if(GameDifficultyConfig.AllOptions.Length!=34)throw new ProtocolException("Game difficulty option layout changed.");
            var values=new object[34];for(int i=0;i<34;i++){var options=MiscDifficultyChoices(GameDifficultyConfig.AllOptions[i]);int index=r.ReadUInt16();if(index>=options.Length)throw new ProtocolException("Invalid difficulty option.");values[i]=options.GetValue(index)!;}r.EnsureEnd();
            var config=d.Resolve<GameDifficultyConfig>().Clone();for(int i=0;i<34;i++)GameDifficultyConfig.AllOptions[i].Property.SetValue(config,values[i]);return config;
        }
        private static void WriteMiscReplacement(ReplaceEntityCmd v,PayloadWriter w)
        {
            WriteDetailEntity(w,v.EntityId);WriteMiscProto(w,v.NewProtoId.Value);w.WriteBoolean(v.ApplyConfiguration);w.WriteBoolean(v.ConfigData.HasValue);
            if(!v.ConfigData.HasValue)return;var data=v.ConfigData.Value;
            var proto=data.Prototype.ValueOrNull as LayoutEntityProto??throw new ProtocolException("Replacement configuration requires a layout building.");
            RequireBlueprintFamily(proto);WriteMiscProto(w,proto.Id.Value);w.WriteBoolean(data.Transform.HasValue);if(data.Transform.HasValue)WriteBuildingTransform(w,data.Transform.Value);
            var settings=CaptureBlueprintSettings(data);ValidateBlueprintSettings(settings,proto is StorageProto);WriteBlueprintSettings(w,settings);
        }
        private static ReplaceEntityCmd ReadMiscReplacement(PayloadReader r,DependencyResolver d)
        {
            var id=ReadEntityId(r);string protoId=ReadScalarProtoId(r);bool apply=r.ReadBoolean();bool hasConfig=r.ReadBoolean();
            string? configProto=null;TileTransform? transform=null;BlueprintSettings? settings=null;
            if(hasConfig){configProto=ReadScalarProtoId(r);if(r.ReadBoolean())transform=ReadBuildingTransform(r);settings=ReadBlueprintSettings(r);}r.EnsureEnd();
            RequireEntity<IUpgradableEntity>(d,id);var proto=RequireResearchTradeProto<StaticEntityProto>(d,protoId);
            if(proto is not IProtoWithUpgrade||!proto.IsUnlockedAndAvailable||proto.CannotBeBuiltByPlayer)throw new ProtocolException("Replacement prototype is unavailable.");
            EntityConfigData? data=null;if(hasConfig){var cp=RequireResearchTradeProto<LayoutEntityProto>(d,configProto!);RequireBlueprintFamily(cp);
                if(cp.Id.Value!=protoId)throw new ProtocolException("Replacement configuration prototype differs from the requested building.");
                ValidateBlueprintSettings(settings!,cp is StorageProto);ValidateBlueprintReferences(settings!,cp,d);
                if(transform.HasValue)RequireMapTile(d,transform.Value.Position.Xy);data=new EntityConfigData(cp,d.Resolve<ConfigSerializationContext>()){Transform=transform};ApplyBlueprintSettings(data,settings!,d);}
            return new ReplaceEntityCmd(id,new Proto.ID(protoId),data,apply);
        }
    }
}
