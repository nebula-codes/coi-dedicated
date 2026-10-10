using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Bridges;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        public const int MaxBridgePlanSteps = 128;
        private static bool TryEncodeBridges(IInputCommand command, PayloadWriter w)
        {
            if (command.GetType() == typeof(CreateBridgeFromPlanCmd))
            {
                var plan = ((CreateBridgeFromPlanCmd)command).Plan;
                if (plan == null || plan.IsStartPlaceHolder) throw new ProtocolException("Bridge plan is a preview placeholder.");
                ValidateRange(plan.Steps.Length,1,MaxBridgePlanSteps,"Bridge segment count");
                w.WriteUInt16(86); w.WriteUInt16((ushort)plan.Steps.Length);
                foreach (var step in plan.Steps)
                {
                    ValidateProtoId(step.Proto.Id.Value); w.WriteString(step.Proto.Id.Value,MaxProtoIdBytes);
                    WritePlanTransform(w,step.Transform);
                }
                return true;
            }
            if (command.GetType() == typeof(SetBridgeLaneLayoutCmd))
            {
                var v = (SetBridgeLaneLayoutCmd)command; WriteManagementEntity(w,87,v.BridgeId);
                WriteBounded(w,v.LaneIndex,0,63,"Bridge lane index"); WriteOptionalProto(w,v.LaneProtoId?.Value); return true;
            }
            return false;
        }
        private static IInputCommand? DecodeBridges(GameCommandKind kind, PayloadReader r, DependencyResolver d)
        {
            if ((ushort)kind==86)
            {
                int count=r.ReadUInt16(); ValidateRange(count,1,MaxBridgePlanSteps,"Bridge segment count");
                var ids=new string[count]; var transforms=new TileTransform[count];
                for(int i=0;i<count;i++){ids[i]=ReadScalarProtoId(r);transforms[i]=ReadPlanTransform(r);}
                r.EnsureEnd(); var steps=new ImmutableArrayBuilder<BridgePlanStep>(count);
                for(int i=0;i<count;i++)
                {
                    var proto=RequireResearchTradeProto<BridgeSegmentProto>(d,ids[i]);
                    if(!proto.BridgeProto.IsUnlockedAndAvailable)throw new ProtocolException("Bridge research is locked.");
                    RequireBuildableLayout(d,ids[i],transforms[i]);
                    steps[i]=new BridgePlanStep(proto,proto.EndDirection.Transformed(transforms[i]),transforms[i]);
                }
                // The native bridge command consumes only each segment's prototype and transform.
                // Recompute geometry from trusted prototypes; do not transmit preview AABBs or planner caches.
                return new CreateBridgeFromPlanCmd(new BridgePlan(steps.GetImmutableArrayAndClear()));
            }
            if ((ushort)kind==87)
            {
                var id=ReadEntityId(r); int lane=ReadBounded(r,0,63,"Bridge lane index"); string? proto=ReadOptionalProto(r);r.EnsureEnd();
                var bridge=RequireEntity<BridgeSegment>(d,id);
                if(lane>=bridge.SingleLanesCount)throw new ProtocolException("Bridge lane does not exist.");
                if(proto!=null && !RequireResearchTradeProto<BridgeLaneProto>(d,proto).IsUnlockedAndAvailable)
                    throw new ProtocolException("Bridge lane research is locked.");
                return new SetBridgeLaneLayoutCmd(id,lane,proto==null?(Proto.ID?)null:new Proto.ID(proto));
            }
            return null;
        }
        private static void WritePlanTransform(PayloadWriter w,TileTransform transform)
        {
            ValidatePlacementScalars(transform.Position,transform.Rotation.AngleIndex);
            w.WriteInt32(transform.Position.X);w.WriteInt32(transform.Position.Y);w.WriteInt32(transform.Position.Z);
            w.WriteInt32(transform.Rotation.AngleIndex);w.WriteBoolean(transform.IsReflected);
        }
        private static TileTransform ReadPlanTransform(PayloadReader r)
        {
            var position=new Tile3i(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());int rotation=r.ReadInt32();bool reflected=r.ReadBoolean();
            ValidatePlacementScalars(position,rotation);return new TileTransform(position,new Rotation90(rotation),reflected);
        }
    }
}
