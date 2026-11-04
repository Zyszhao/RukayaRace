using System.Collections.Generic;
using RimWorld;
using RukayaRace.Buildings;
using Verse;

namespace RukayaRace.Flight
{
    /// <summary>
    /// Render node for Rukaya wing nodes. It exists so the render tree XML names a concrete node type
    /// instead of relying on the base class default, which keeps the node side of the wing chain readable.
    ///
    /// The base class caches <c>graphicStateLookup</c> (the map from <see cref="GraphicStateDef"/> to
    /// <see cref="Graphic"/>) the first time the tree is drawn, and <c>PawnRenderNode.StateGraphicsFor</c>
    /// only collects the states of whichever animation was current at that moment. That is fine here as long
    /// as nothing ever changes the playing animation without also dirtying the tree: a dirty tree discards
    /// every node and hands out fresh ones, which collect the current animation's frames. The one place that
    /// changes the animation is <see cref="CompRukayaFlightControl.AlignWingAnimation"/>, which goes through
    /// <c>PawnRenderer.SetAnimation</c> - and that always ends in <c>PawnRenderTree.SetDirty</c>. So the
    /// lookup is guaranteed to match the animation, and no per-frame rebuild is needed.
    /// </summary>
    public class PawnRenderNode_RukayaWing : PawnRenderNode
    {
        public PawnRenderNode_RukayaWing(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }
    }

    /// <summary>
    /// Decides, per draw pass, whether a Rukaya wing node is visible. Everything it checks is deliberately
    /// evaluated every frame instead of being snapshotted when the node is constructed: a wing that is shot
    /// off has to disappear, and - just as important - it has to come back on its own once the part is
    /// healed, which a construction-time snapshot cannot do because nothing rebuilds the render tree when a
    /// hediff is removed.
    ///
    /// Two families of nodes are served, and they are strict complements so a static wing is never painted
    /// on top of a flap frame:
    ///   * static wings (tags Rukaya_WingBack / Rukaya_WingFront): drawn while the pawn is not flapping.
    ///   * flap anchors (tags Rukaya_WingFlapBack / Rukaya_WingFlapFront): drawn only while the pawn is
    ///     actually flapping. Their graphic comes from the animation keyframes, not from a texPath.
    ///
    /// Both families pass through the same wing-presence gate. That matters for the flap anchors: the
    /// authored flap frames are *composites of both wings*, so a frame cannot honour "one side was shot off"
    /// by itself. Without the gate a pawn that lost a
    /// wing mid-air kept painting full two-wing frames over the stump - which is exactly the "wing root
    /// leftovers after damage" report. The gate lives here rather than in the animation because a keyframe
    /// graphic is a whole-pawn sprite, not a per-part one.
    ///
    /// The vanilla halves of the visibility problem (<c>rotDrawMode</c>, <c>visibleFacing</c>, <c>skipFlag</c>)
    /// are left to <c>base.CanDrawNow</c>, so apparel flagged with <c>Rukaya_HideWings</c> keeps working
    /// through the stock render-skip-flag mechanism.
    ///
    /// Note: GenWorker caches one instance per worker type, so this class must stay stateless - everything
    /// node-specific has to be read from <paramref name="node"/> on each call.
    ///
    /// 帧时序不在这里：扑翼每一拍该画哪张图由 <c>1.7/Defs/AnimationDefs/Rukaya_WingFly.xml</c> 的
    /// 关键帧决定，现值（每帧 4 tick、一轮 32 tick，8 帧严格等长）**工作正常、已被启动期校验固化**
    /// （<see cref="RukayaWingAnimationValidator"/>，日志关键字「扑翼校验」）。
    /// 本类只负责"画不画"，不要为了修观感去改这里的可见性判定，也不要改动画的 tick ——
    /// 时序不变量见 Rukaya_WingFly.xml 文件头。
    /// </summary>
    public class PawnRenderNodeWorker_RukayaWing : PawnRenderNodeWorker
    {
        /// <summary>
        /// Which wing surfaces are still on the pawn. A bitmask rather than a two-value enum because the two
        /// sides are independent body parts: either, both or neither can be missing.
        /// </summary>
        [System.Flags]
        public enum Wings
        {
            None = 0,
            Left = 1,
            Right = 2,
            Both = Left | Right
        }

        /// <summary>Which half of the wing pair a static node represents.</summary>
        private enum WingSide
        {
            None,
            Left,
            Right
        }

        private static readonly BodyPartDef WingPartDef = DefDatabase<BodyPartDef>.GetNamedSilentFail("Rukaya_Wing");

        private const string LeftWingLabel = "left wing";
        private const string RightWingLabel = "right wing";

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
            {
                return false;
            }

            Pawn pawn = parms.pawn;
            if (pawn == null || node?.Props == null)
            {
                return false;
            }

            if (pawn.GetPosture().InBed() && RukayaHangingSleep.IsCurtainedBed(pawn.CurrentBed()?.def))
            {
                return false;
            }

            bool flapNode = IsFlapTag(node.Props.tagDef);
            bool flapping = CompRukayaFlightControl.IsPlayingFlapAnimation(pawn);

            // Requirement R3: while the flap frames play, the static wings must not be drawn at all.
            if (flapNode != flapping)
            {
                return false;
            }

            // The shared presence gate. One lookup serves both families, so a node can never disagree with
            // its siblings about whether the pawn still has that wing.
            Wings present = PresentWings(pawn);

            if (flapNode)
            {
                // A flap frame is a composite of both wings, so it may only be painted while both surfaces it
                // depicts still exist. A pawn that lost a wing stops flying (BothWings is the gate on the flight
                // decision too), and this is what stops full two-wing frames from being drawn over the stump
                // while it descends.
                return present == Wings.Both;
            }

            WingSide side = SideForPath(node.Props.texPath);
            switch (side)
            {
                case WingSide.Left:
                    return (present & Wings.Left) != 0;
                case WingSide.Right:
                    return (present & Wings.Right) != 0;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Every wing surface still attached and undestroyed. Single source of truth for the per-side verdict:
        /// the render worker, the flight gate and the DevMode diagnostics all read the same computed value, so
        /// "the pawn flaps" and "the wing is drawn" can never disagree.
        ///
        /// Matched on the <see cref="BodyPartDef"/> plus the untranslated custom label, because the left and
        /// right wing share a single def and are only told apart by that label.
        /// </summary>
        public static Wings PresentWings(Pawn pawn)
        {
            BodyDef body = pawn?.RaceProps?.body;
            if (body == null || WingPartDef == null || pawn.health?.hediffSet == null)
            {
                // No body or no health system means there is nothing that could have removed a wing. Report
                // everything present so a half-built pawn never renders wingless by accident.
                return Wings.Both;
            }

            bool left = false;
            bool right = false;
            List<BodyPartRecord> parts = body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (part == null || part.def != WingPartDef)
                {
                    continue;
                }

                if (pawn.health.hediffSet.PartIsMissing(part))
                {
                    continue;
                }

                if (part.untranslatedCustomLabel == LeftWingLabel)
                {
                    left = true;
                }
                else if (part.untranslatedCustomLabel == RightWingLabel)
                {
                    right = true;
                }
            }

            return (left ? Wings.Left : Wings.None) | (right ? Wings.Right : Wings.None);
        }

        /// <summary>True when the named tag is one of the two flap-anchor tags.</summary>
        public static bool IsFlapTag(PawnRenderNodeTagDef tag)
        {
            return tag != null
                && (tag == RukayaWingRenderDefOf.Rukaya_WingFlapBack
                    || tag == RukayaWingRenderDefOf.Rukaya_WingFlapFront);
        }

        /// <summary>
        /// The node's side is pure static XML config, but it cannot be cached on the worker (one shared
        /// instance serves every node of this worker type), so it is read back from the texPath each call.
        /// </summary>
        private static WingSide SideForPath(string path)
        {
            if (path.NullOrEmpty())
            {
                return WingSide.None;
            }

            if (path.Contains("LeftWing"))
            {
                return WingSide.Left;
            }

            if (path.Contains("RightWing"))
            {
                return WingSide.Right;
            }

            return WingSide.None;
        }

    }
}
