using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Core.Terrain;
using Mafi.Core.Trains;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        public const int MaxRailPlanSteps = 128;
        public const int MaxSupportsPerRailStep = 64;

        private static bool TryEncodeRails(IInputCommand command, PayloadWriter w)
        {
            if(command.GetType()!=typeof(CreateTrainTrackFromPlanCmd))return false;
            var plan=((CreateTrainTrackFromPlanCmd)command).Plan;
            // PolesValid is advisory planner state: the native build controller can commit a
            // supported track even when electrification support search has not resolved its poles.
            if(plan==null || plan.IsStartPlaceHolder || !plan.PillarsValid)
                throw new ProtocolException("Rail plan is incomplete or has invalid supports.");
            ValidateRange(plan.Steps.Length,1,MaxRailPlanSteps,"Rail segment count");
            w.WriteUInt16(88);WriteBounded(w,(int)plan.BuildDirection,1,3,"Rail direction");
            WriteOptionalRailPole(w,plan.PoleAtStartIndex);WriteOptionalRailPole(w,plan.PoleAtEndIndex);
            w.WriteUInt16((ushort)plan.Steps.Length);
            foreach(var step in plan.Steps)
            {
                ValidateProtoId(step.Proto.Id.Value);w.WriteString(step.Proto.Id.Value,MaxProtoIdBytes);WritePlanTransform(w,step.Transform);
                WriteBounded(w,(int)step.TrackDirection,1,3,"Segment direction");
                ValidateRange(step.PillarInfos.Length,0,MaxSupportsPerRailStep,"Pillar count");w.WriteUInt16((ushort)step.PillarInfos.Length);
                var pillars=step.Proto.GetTransformedPillarsData(step.Transform.Transform90RotFlip);
                foreach(var pillar in step.PillarInfos)
                {
                    int found=-1;for(int i=0;i<pillars.Length;i++)if(pillars[i].Equals(pillar.InfoRel)){found=i;break;}
                    if(found<0 || pillar.Offset.X!=step.Transform.Position.X || pillar.Offset.Y!=step.Transform.Position.Y)
                        throw new ProtocolException("Rail pillar does not belong to its segment prototype.");
                    w.WriteUInt16((ushort)found);
                    WriteBounded(w,pillar.Offset.Z.RawValue,short.MinValue*1024,short.MaxValue*1024,"Pillar ground height");
                    WriteBounded(w,pillar.Height.Value.RawValue,short.MinValue*1024,short.MaxValue*1024,"Pillar height");
                }
                ValidateRange(step.PoleInfos.Length,0,MaxSupportsPerRailStep,"Pole count");w.WriteUInt16((ushort)step.PoleInfos.Length);
                foreach(var pole in step.PoleInfos)
                {
                    var candidates=step.Proto.GetTransformedPolesData(step.Transform.Transform90RotFlip,pole.InfoRel.IsClose);
                    int found=-1;for(int i=0;i<candidates.Length;i++)if(candidates[i].Equals(pole.InfoRel)){found=i;break;}
                    if(found<0 || pole.TrackPosition!=step.Transform.Position || pole.InfoRel.IsStartEnd)
                        throw new ProtocolException("Rail pole does not belong to its segment prototype.");
                    w.WriteBoolean(pole.InfoRel.IsClose);w.WriteUInt16((ushort)found);
                }
            }
            return true;
        }
        private static IInputCommand? DecodeRails(GameCommandKind kind,PayloadReader r,DependencyResolver d)
        {
            if((ushort)kind!=88)return null;
            int direction=ReadBounded(r,1,3,"Rail direction");int? start=ReadOptionalRailPole(r),end=ReadOptionalRailPole(r);
            int count=r.ReadUInt16();ValidateRange(count,1,MaxRailPlanSteps,"Rail segment count");
            var ids=new string[count];var transforms=new TileTransform[count];var directions=new int[count];
            var pillarIndices=new int[count][];var pillarGround=new int[count][];var pillarHeight=new int[count][];
            var poleIndices=new int[count][];var poleClose=new bool[count][];
            for(int i=0;i<count;i++)
            {
                ids[i]=ReadScalarProtoId(r);transforms[i]=ReadPlanTransform(r);directions[i]=ReadBounded(r,1,3,"Segment direction");
                int n=r.ReadUInt16();ValidateRange(n,0,MaxSupportsPerRailStep,"Pillar count");
                pillarIndices[i]=new int[n];pillarGround[i]=new int[n];pillarHeight[i]=new int[n];
                for(int j=0;j<n;j++)
                {
                    pillarIndices[i][j]=r.ReadUInt16();
                    pillarGround[i][j]=ReadBounded(r,short.MinValue*1024,short.MaxValue*1024,"Pillar ground height");
                    pillarHeight[i][j]=ReadBounded(r,short.MinValue*1024,short.MaxValue*1024,"Pillar height");
                    for(int k=0;k<j;k++)if(pillarIndices[i][k]==pillarIndices[i][j])throw new ProtocolException("Duplicate rail pillar.");
                }
                n=r.ReadUInt16();ValidateRange(n,0,MaxSupportsPerRailStep,"Pole count");poleIndices[i]=new int[n];poleClose[i]=new bool[n];
                for(int j=0;j<n;j++)
                {
                    poleClose[i][j]=r.ReadBoolean();poleIndices[i][j]=r.ReadUInt16();
                    for(int k=0;k<j;k++)if(poleIndices[i][k]==poleIndices[i][j] && poleClose[i][k]==poleClose[i][j])throw new ProtocolException("Duplicate rail pole.");
                }
            }
            r.EnsureEnd();var steps=new ImmutableArrayBuilder<TrainTrackPlanStep>(count);
            for(int i=0;i<count;i++)
            {
                var proto=RequireResearchTradeProto<TrainTrackProto>(d,ids[i]);var transform=transforms[i];
                if(!proto.IsUnlockedAndAvailable || proto.CannotBeBuiltByPlayer || proto.IsAutoBuiltByParent)
                    throw new ProtocolException("Rail segment is locked or cannot be built by players.");
                if(transform.IsReflected && proto.CannotBeReflected)throw new ProtocolException("Rail segment cannot be reflected.");
                var bounds=proto.Layout.GetBoundingBox2i(transform);RequireMapTile(d,bounds.Origin);RequireMapTile(d,bounds.PlusXyTileIncl);
                var nativePillars=proto.GetTransformedPillarsData(transform.Transform90RotFlip);
                var pillars=new ImmutableArrayBuilder<TrainTrackPillarInfo>(pillarIndices[i].Length);
                for(int j=0;j<pillarIndices[i].Length;j++)
                {
                    int index=pillarIndices[i][j];if(index>=nativePillars.Length)throw new ProtocolException("Rail pillar index does not exist.");
                    var info=nativePillars[index];
                    if(!d.Resolve<TrainTracksPillarManager>().CanPlacePillar(transform.Position,info,out var height,out var ground))
                        throw new ProtocolException("Rail pillar cannot be placed on the current terrain.");
                    if(ground.Value.RawValue!=pillarGround[i][j] || height.Value.RawValue!=pillarHeight[i][j])
                        throw new ProtocolException("Terrain under a rail pillar changed; place the plan again.");
                    pillars[j]=new TrainTrackPillarInfo(transform.Position.CornerTile3f.Xy.ExtendHeight(ground),height,info);
                }
                var poles=new ImmutableArrayBuilder<TrainTrackPoleInfo>(poleIndices[i].Length);
                for(int j=0;j<poleIndices[i].Length;j++)
                {
                    var nativePoles=proto.GetTransformedPolesData(transform.Transform90RotFlip,poleClose[i][j]);int index=poleIndices[i][j];
                    if(index>=nativePoles.Length)throw new ProtocolException("Rail pole index does not exist.");
                    poles[j]=new TrainTrackPoleInfo(transform.Position,nativePoles[index]);
                }
                if(i==0 && start.HasValue && !proto.GetTransformedStartEndPolesData(transform.Transform90RotFlip)[start.Value].HasValue)
                    throw new ProtocolException("Rail start pole does not exist.");
                if(i==count-1 && end.HasValue && !proto.GetTransformedStartEndPolesData(transform.Transform90RotFlip)[end.Value].HasValue)
                    throw new ProtocolException("Rail end pole does not exist.");
                steps[i]=new TrainTrackPlanStep(proto,proto.GetTransformedTrajectory(transform.Transform90RotFlip).EndDirection,
                    (TrainTrackTrajectoryDirection)directions[i],transform,pillars.GetImmutableArrayAndClear(),poles.GetImmutableArrayAndClear());
            }
            // Support geometry and masks come from trusted prototype data. Heights must match both
            // the originating plan and the current island; never silently change a local pending command.
            // The native processor retains construction costs, graph merging and upgrade validation.
            return new CreateTrainTrackFromPlanCmd(new TrainTrackPlan(steps.GetImmutableArrayAndClear(),true,true,
                poleAtStartIndex:start,poleAtEndIndex:end,options:new TrainTrackPathFinderOptions(buildDirection:(TrainTrackTrajectoryDirection)direction)));
        }
        private static void WriteOptionalRailPole(PayloadWriter w,int? index)
        {w.WriteBoolean(index.HasValue);if(index.HasValue)WriteBounded(w,index.Value,0,3,"Rail endpoint pole");}
        private static int? ReadOptionalRailPole(PayloadReader r)=>r.ReadBoolean()?ReadBounded(r,0,3,"Rail endpoint pole"):(int?)null;
    }
}
