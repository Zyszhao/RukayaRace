using RimWorld;
using RukayaRace.AirDefense;
using RukayaRace.Weapons;
using UnityEngine;
using Verse;

namespace RukayaRace.Buildings
{
    /// <summary>
    /// 放置预览时绘制炮塔的射程/最小射程环，以及**对空绿色范围圈**。
    ///
    /// 原版 <c>PlaceWorker_ShowTurretRadius.AllowsPlacing</c> 用
    /// <c>v.verbClass == typeof(Verb_Shoot)</c> 做精确类型匹配来查找 verb：
    /// 任何自定义 verb 子类都匹配不上，<c>List.Find</c> 返回 null 后立刻访问
    /// <c>verbProperties.range</c>，于是放置预览每帧抛 NullReferenceException。
    /// 这里复刻同样的效果，但按 <c>Verb_LaunchProjectile</c> 家族匹配并对空值防御。
    ///
    /// 对空圈（需求方 2026-10-08 反馈）：挂了 <see cref="CompProperties_RukayaSkyInterceptor"/> 的建筑
    /// 在**放置预览**阶段就要画出绿色防空半径，而不再只有建成选中后才看得到
    /// （<c>CompRukayaSkyInterceptor.PostDrawExtraSelectionOverlays</c> 只对已建成的建筑生效）。
    /// 颜色与选中时一致，且只在该组件默认开启对空模式时画——否则预览会承诺一份开局并不存在的覆盖。
    /// </summary>
    public class PlaceWorker_RukayaTurretRadius : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef thingDef = checkingDef as ThingDef;
            if (thingDef?.building?.turretGunDef != null)
            {
                VerbProperties verbProperties = thingDef.building.turretGunDef.Verbs.Find(
                    v => v.verbClass != null && typeof(Verb_LaunchProjectile).IsAssignableFrom(v.verbClass));
                if (verbProperties != null)
                {
                    if (verbProperties.range > 0f)
                    {
                        GenDraw.DrawRadiusRing(loc, verbProperties.range);
                    }
                    if (verbProperties.minRange > 0f)
                    {
                        GenDraw.DrawRadiusRing(loc, verbProperties.minRange);
                    }
                }
            }
            if (thingDef?.comps != null)
            {
                foreach (CompProperties comp in thingDef.comps)
                {
                    if (comp is CompProperties_RukayaSkyInterceptor airDefense && airDefense.airModeOnDefault
                        && airDefense.airDefenseRange > 0f)
                    {
                        GenDraw.DrawRadiusRing(loc, airDefense.airDefenseRange,
                            CompRukayaSkyInterceptor.AirRangeRingColorActive);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 放置预览里的**炮管层**。
        ///
        /// 原版 <c>PlaceWorker_TurretTop</c> 只画炮塔顶（它读 <c>turretGunDef.graphicData.texPath</c>，
        /// 也就是我们的 _turret_top），所以在预览里炮塔会缺了那两根长炮管、和建成后的外观对不上。
        /// 这里补上同一层的炮管，口径与 <see cref="Building_RukayaNavalArtillery"/> 完全一致：
        /// 同一个 texPath（<c>RukayaNavalArtilleryExtension.barrelTexPath</c>）、同一个 drawSize
        /// （<c>building.turretTopDrawSize</c>）、同一个 <see cref="TurretTop.ArtworkRotation"/>，
        /// 只是高度降低半个 <see cref="Altitudes.AltIncVect"/>，好让炮塔顶的 ghost 压住炮管根部。
        ///
        /// 只有配了炮管的 def 才画；没配（或贴图缺失）时直接返回，预览退回原版表现。
        /// </summary>
        public override void DrawGhost(ThingDef def, IntVec3 loc, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            RukayaNavalArtilleryExtension extension = def?.GetModExtension<RukayaNavalArtilleryExtension>();
            if (extension == null || extension.barrelTexPath.NullOrEmpty() || def.building == null)
                return;
            if (ContentFinder<Texture2D>.Get(extension.barrelTexPath, false) == null)
                return;
            float drawSize = def.building.turretTopDrawSize;
            Graphic barrelGhost = GhostUtility.GhostGraphicFor(
                GraphicDatabase.Get<Graphic_Single>(extension.barrelTexPath, ShaderDatabase.Cutout,
                    new Vector2(drawSize, drawSize), Color.white),
                def, ghostCol, null);
            Vector3 center = GenThing.TrueCenter(loc, rot, def.Size, AltitudeLayer.MetaOverlays.AltitudeFor())
                - Altitudes.AltIncVect * 0.5f;
            barrelGhost.DrawFromDef(center, rot, def, TurretTop.ArtworkRotation);
        }
    }
}
