using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RukayaRace.Scenario
{
    /// <summary>
    /// 剧本事件"三名同伴到来"：三人一批乘运输舱落到殖民地附近，并立即加入玩家。
    ///
    /// 生成沿用原版 <see cref="IncidentWorker_WandererJoin.GeneratePawn"/> 的请求参数：
    /// 玩家阵营、<c>colonistRelationChanceFactor = 20</c>（因此有可能与已有殖民者是亲属）、
    /// 特质 / 背景故事 / 服装随机，意识形态固定为玩家主文化；姓氏由本模组姓库随机给出。
    /// 名字连同短名（Nick）一起改写，并设置固定武器、指定伤疤与持续半天的麻痹症。
    /// </summary>
    public class IncidentWorker_RukayaTrioArrival : IncidentWorker_WandererJoin
    {
        /// <summary>
        /// 伤疤材质：枪伤。原版 <c>HediffDefOf</c> 里没有收录 Gunshot，只能按名字取
        /// （Core 自带该 Def，不需要 MayRequire）。它的 <c>HediffCompProperties_GetsPermanent</c>
        /// 提供 <c>old gunshot</c> 这一永久标签，正好用来当伤疤。
        /// </summary>
        private static readonly HediffDef ScarHediff = DefDatabase<HediffDef>.GetNamed("Gunshot");

        private const int ArrivalAbasiaDurationTicks = GenDate.TicksPerDay / 2;

        public override Pawn GeneratePawn(Map map)
        {
            // Vanilla WandererJoin deliberately prefers an ideology outside the player's
            // faction. Set FixedIdeo before generation so gear and ideology agree, too.
            Gender? gender = def.pawnFixedGender == Gender.None ? null : def.pawnFixedGender;
            return PawnGenerator.GeneratePawn(new PawnGenerationRequest(def.pawnKind,
                faction: Faction.OfPlayer, context: PawnGenerationContext.NonPlayer, tile: map?.Tile,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: true, mustBeCapableOfViolence: def.pawnMustBeCapableOfViolence,
                colonistRelationChanceFactor: 20f, fixedGender: gender,
                fixedIdeo: ModsConfig.IdeologyActive ? Faction.OfPlayer.ideos?.PrimaryIdeo : null));
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            IncidentDef_RukayaTrioArrival def = this.def as IncidentDef_RukayaTrioArrival;
            if (def == null || def.firstNames.NullOrEmpty())
            {
                Log.Error("[Rukaya] " + this.def.defName + " 缺少 firstNames，未刷出任何同伴。");
                return false;
            }

            Map map = parms.target as Map;
            if (map == null)
            {
                map = Find.AnyPlayerHomeMap;
            }
            if (map == null)
            {
                return false;
            }

            List<Pawn> arrivals = new List<Pawn>();
            for (int i = 0; i < def.firstNames.Count; i++)
            {
                Pawn pawn = this.GeneratePawn(map);
                if (pawn == null)
                {
                    Log.Error("[Rukaya] " + this.def.defName + " 生成同伴失败，跳过一人。");
                    continue;
                }
                ConfigureCompanion(pawn, i);
                arrivals.Add(pawn);
            }
            if (arrivals.Count == 0)
            {
                return false;
            }

            // 降落点跟随玩家基地（有轨道贸易信标就用信标附近，否则用基地建筑群附近），
            // 与原版玩家开局"坠落舱着陆"用的是同一套投放机制。
            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            if (!dropSpot.IsValid)
            {
                dropSpot = DropCellFinder.RandomDropSpot(map, false);
            }
            // forbid:false —— 舱里装的是人不是货物，不要给 pawn 打上"禁止"标记。
            DropPodUtility.DropThingsNear(dropSpot, map, arrivals, 110, false, false, true, false, true, Faction.OfPlayer);

            // 需求方 2026-10-08 实测反馈：信件正文只显示名，不显示姓氏。
            string arrivedNames = string.Join(", ", arrivals.Select(FirstNameOf));
            this.SendStandardLetter(parms, new LookTargets(arrivals), arrivedNames.Named("PAWNS"));
            return true;
        }

        // Shared identity/equipment setup; annual arrivals use neutral generation instead
        // of GeneratePawn's player-faction generation, but keep the same three presets.
        public void ConfigureCompanion(Pawn pawn, int index)
        {
            var config = (IncidentDef_RukayaTrioArrival)def;
            ForceFirstName(pawn, config.firstNames[index]);
            StripInjuriesAndArtificialParts(pawn);
            EquipFixedWeapon(pawn, ElementOrDefault(config.weapons, index));
            if (ElementOrDefault(config.scarHeadAndEye, index))
            {
                AddMinorScar(pawn, BodyPartDefOf.Head);
                AddMinorScarToEveryEye(pawn);
            }
            ApplyArrivalAbasia(pawn);
        }

        /// <summary>每位同伴必定带有半天麻痹症；已有同种疾病时重设计时，避免重复添加。</summary>
        private static void ApplyArrivalAbasia(Pawn pawn)
        {
            Hediff abasia = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Abasia)
                ?? pawn.health.AddHediff(HediffDefOf.Abasia);
            abasia.TryGetComp<HediffComp_Disappears>().SetDuration(ArrivalAbasiaDurationTicks);

            // 原版在首次 tick 即可能抽中提前康复；本次麻痹到期前不进行该抽签。
            HediffComp_ChanceToRemove chanceToRemove = abasia.TryGetComp<HediffComp_ChanceToRemove>();
            chanceToRemove.currentInterval = ArrivalAbasiaDurationTicks;
            chanceToRemove.removeNextInterval = false;
        }

        /// <summary>取 pawn 的名（不带姓），用于信件正文列举。</summary>
        private static string FirstNameOf(Pawn pawn)
        {
            NameTriple triple = pawn.Name as NameTriple;
            if (triple != null && !triple.First.NullOrEmpty())
            {
                return triple.First;
            }
            return pawn.Name.ToStringShort;
        }

        /// <summary>
        /// 只覆盖 firstname（以及命名器镜像出来的短名），姓氏与其余一切保持随机生成的结果。
        ///
        /// 注意本族的取名通道：<c>Alien_Rukaya</c> 的 <c>&lt;race&gt;&lt;nameGenerator&gt;</c> 指向
        /// <c>NamerPerson_Rukaya</c>，而原版 <c>PawnBioAndNameGenerator.GenerateFullPawnName</c>
        /// 在 <c>nameGenner != null</c> 时返回的是 <c>NameSingle</c>——也就是把规则串
        /// <c>ShipName FirstName</c> 原样装进一个单名里。所以这里不能假设 <c>pawn.Name</c> 是
        /// NameTriple：先按 NameTriple 取姓，否则把 NameSingle 解析回两段并保留首段舰船名。
        /// 这样三位固定名同伴继续显示为不带昵称的 "First Last"，不随一般角色的倒序规则变化。
        /// 姓一律不允许为 null —— <c>NameTriple</c> 的构造函数会对 last 调 <c>Trim()</c>。
        /// </summary>
        private static void ForceFirstName(Pawn pawn, string firstName)
        {
            if (firstName.NullOrEmpty())
            {
                return;
            }
            string lastName = ExtractLastName(pawn);
            pawn.Name = new NameTriple(firstName, firstName, lastName.NullOrEmpty() ? "" : lastName);
        }

        /// <summary>按下标取表里的值；表缺项或越界时返回 default（武器为 null、伤疤为 false）。</summary>
        private static T ElementOrDefault<T>(List<T> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                return default(T);
            }
            return list[index];
        }

        /// <summary>
        /// 清掉生成管线给的伤疤 / 旧伤（<see cref="Hediff_Injury"/>，原版由
        /// <c>AgeInjuryUtility.GenerateRandomOldAgeInjuries</c> 产生）与仿生体 / 人造部件
        /// （<c>HediffDef.addedPartProps</c> 非空，原版由 <c>PawnTechHediffsGenerator</c> 产生）。
        /// 断肢（<c>Hediff_MissingPart</c>）不属于这两类，保持原样。
        /// </summary>
        private static void StripInjuriesAndArtificialParts(Pawn pawn)
        {
            List<Hediff> toRemove = new List<Hediff>();
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff is Hediff_Injury || hediff.def.addedPartProps != null)
                {
                    toRemove.Add(hediff);
                }
            }
            foreach (Hediff hediff in toRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        /// <summary>事件生成时自带武器：与开局剧本发的枪无关，直接装备到该角色身上。</summary>
        private static void EquipFixedWeapon(Pawn pawn, ThingDef weaponDef)
        {
            if (weaponDef == null || pawn.equipment == null || pawn.equipment.Primary != null)
            {
                return;
            }
            ThingWithComps weapon = ThingMaker.MakeThing(weaponDef, null) as ThingWithComps;
            if (weapon == null)
            {
                Log.Error("[Rukaya] " + weaponDef.defName + " 不是可装备的 ThingWithComps，未发放武器。");
                return;
            }
            pawn.equipment.AddEquipment(weapon);
        }

        /// <summary>
        /// 给指定部位加一道轻伤疤（<b>枪伤</b>）：照抄原版 <c>PawnGenerator.ApplyMiscDamage</c> 的写法 ——
        /// 先造一个 <c>HediffDefOf.Gunshot</c> 伤，再把 <c>HediffComp_GetsPermanent.IsPermanent</c>
        /// 置真（永久 = 伤疤），并把疼痛类别设为 Painless，使其几乎不影响数值与心情。
        /// 2026-10-08 需求方反馈：伤疤材质由割伤改为枪伤。
        /// </summary>
        private static void AddMinorScar(Pawn pawn, BodyPartDef partDef)
        {
            BodyPartRecord part = pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def == partDef)
                .RandomElementWithFallback(null);
            if (part == null)
            {
                Log.Warning("[Rukaya] 找不到部位 " + partDef.defName + "，未添加伤疤。");
                return;
            }
            AddScarToPart(pawn, part);
        }

        /// <summary>
        /// 给**每一只**眼睛都加一道轻伤疤。
        ///
        /// 本族身体里左右眼是同 Def 的两条 <c>BodyPartRecord</c>（Bodies.xml 的 customLabel =
        /// left eye / right eye），所以不能对 <c>BodyPartDefOf.Eye</c> 用
        /// <c>RandomElementWithFallback</c> 随机取一条 —— 那样只会随机落在左眼或右眼，
        /// 表现为"有时两只眼睛先后各中一次"。这里按记录逐条加，保证两眼的疤成对出现。
        /// </summary>
        private static void AddMinorScarToEveryEye(Pawn pawn)
        {
            List<BodyPartRecord> eyes = pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def == BodyPartDefOf.Eye)
                .ToList();
            if (eyes.Count == 0)
            {
                Log.Warning("[Rukaya] 找不到部位 " + BodyPartDefOf.Eye.defName + "，未添加眼部伤疤。");
                return;
            }
            foreach (BodyPartRecord eye in eyes)
            {
                AddScarToPart(pawn, eye);
            }
        }

        private static void AddScarToPart(Pawn pawn, BodyPartRecord part)
        {
            Hediff hediff = HediffMaker.MakeHediff(ScarHediff, pawn, part);
            hediff.Severity = 1f;
            HediffComp_GetsPermanent getsPermanent = hediff.TryGetComp<HediffComp_GetsPermanent>();
            if (getsPermanent != null)
            {
                getsPermanent.IsPermanent = true;
                getsPermanent.SetPainCategory(PainCategory.Painless);
            }
            pawn.health.AddHediff(hediff, part, null, null);
        }

        /// <summary>从新命名器的两段式结果中取出舰船姓氏；取不到时返回空串（不返回 null）。</summary>
        private static string ExtractLastName(Pawn pawn)
        {
            NameTriple triple = pawn.Name as NameTriple;
            if (triple != null)
            {
                return triple.Last;
            }
            NameSingle single = pawn.Name as NameSingle;
            if (single == null || single.Name.NullOrEmpty())
            {
                return "";
            }
            string parsed = NameTriple.FromString(single.Name).Last;
            if (parsed.NullOrEmpty())
            {
                Log.Warning("[Rukaya] 无法从取名结果 \"" + single.Name + "\" 中解析出姓氏，该同伴将没有姓氏。");
            }
            return parsed;
        }
    }
}
