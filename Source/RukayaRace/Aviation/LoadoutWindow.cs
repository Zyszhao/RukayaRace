using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaAviation;

public class LoadoutWindow : Window
{
    private readonly Aircraft plane;
    private string status = "";
    public override Vector2 InitialSize => new Vector2(plane.IsInterceptor ? 1260 : plane.IsTransport ? 880 : 1060, 760);
    public LoadoutWindow(Aircraft plane)
    {
        this.plane = plane; doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true;
    }
    public override void DoWindowContents(Rect rect)
    {
        if (plane.Destroyed) { Close(); return; }
        float width = rect.width - 24, column = width / plane.mounts.Count;
        Text.Font = GameFont.Medium; Widgets.Label(new Rect(12, 8, 900, 38), plane.LabelCap + " — 挂载配置"); Text.Font = GameFont.Small;
        Widgets.Label(new Rect(12, 50, 900, 50), $"货舱 {plane.Cargo.MassUsage:F0}/{plane.CargoCapacity:F0} kg  ·  挂载 {plane.MountWeight:F0} kg  ·  起飞上限 {plane.TakeoffLimit:F0} kg\n乘员 {plane.Cargo.innerContainer.OfType<Pawn>().Count()}/{plane.Seats}  ·  航程 {plane.Range}  ·  速度 {plane.Speed:F1} 格/秒  ·  载荷效率 {plane.FlightPerformance:P0}");
        Widgets.DrawBoxSolid(new Rect(12, 105, width, 226), new Color(0.09f, 0.12f, 0.15f));
        GUI.DrawTexture(new Rect(12 + width / 2 - 200, 107, 400, 220), plane.def.uiIcon, ScaleMode.ScaleToFit);
        Widgets.Label(new Rect(30, 122, 290, 75), plane.IsTransport
            ? "1—2号挂点（机翼外侧）\n可挂武器、副油箱或吊舱"
            : plane.Heavy ? "1—7号挂点\n3、4、5号可挂副油箱与吊舱" : "1—5号挂点，含固定机炮\n3号仅限副油箱与吊舱");
        Widgets.Label(new Rect(width - 266, 122, 270, 75), plane.IsTransport
            ? "不可挂活性物质导弹\n需2名飞行员"
            : plane.Heavy ? "4号挂点双倍备弹\n活性物质导弹限4号" : "垂直起飞：1100 kg / 18燃料\n跑道起飞：1800 kg / 5燃料");
        for (int i = 0; i < plane.mounts.Count; i++)
        {
            int index = i; Hardpoint m = plane.mounts[i];
            float y = 344; float x = 12 + i * column, cardWidth = column - 6;
            Rect card = new Rect(x, y, cardWidth, 144); Widgets.DrawMenuSection(card);
            Widgets.Label(new Rect(x + 8, y + 8, cardWidth - 16, 24), $"挂点 {i + 1}  ·  {m.Weight:F0} kg");
            if (m.kind != StoreKind.Empty) GUI.DrawTexture(new Rect(x + 8, y + 38, 32, 36), AV.Icon(Hardpoint.Texture(m.kind)), ScaleMode.ScaleToFit);
            if (Widgets.ButtonText(new Rect(x + 38, y + 38, cardWidth - 44, 36), Hardpoint.Name(m.kind).Split('（')[0]))
            {
                if (plane.State != FlightState.Parked) status = "请先降落。";
                else Find.WindowStack.Add(new FloatMenu(Enum.GetValues<StoreKind>().Where(k => plane.CanMount(index, k)).Select(k => new FloatMenuOption(Hardpoint.Name(k), () => Install(index, k))).ToList()));
            }
            Widgets.Label(new Rect(x + 8, y + 80, cardWidth - 16, 24), $"备弹 {m.rounds}/{plane.StoreCapacity(i, m.kind)}");
            if (plane.StoreCapacity(i, m.kind) > 0)
            {
                Rect rearmRect = new Rect(x + 8, y + 108, cardWidth - 16, 28);
                if (Widgets.ButtonText(rearmRect, "补充弹药")) Rearm(index);
                var costs = Ordnance.RearmCost(m.kind, Math.Max(0, plane.StoreCapacity(i, m.kind) - m.rounds));
                TooltipHandler.TipRegion(rearmRect, costs.Any(c => c.count > 0) ? "补满需要：" + string.Join("、", costs.Where(c => c.count > 0).Select(c => $"{c.count} {c.def.LabelCap}")) : "弹药已满。");
            }
            float dotX, dotY;
            if (plane.IsTransport)
            {
                dotX = 12 + width / 2 - 12 + (i == 0 ? -42f : 42f);
                dotY = 212f;
            }
            else
            {
                dotX = 12 + width / 2 - 12 + (i - plane.CenterHardpoint) * 32;
                dotY = 242 + (plane.CenterHardpoint - Mathf.Abs(i - plane.CenterHardpoint)) * 12;
            }
            Widgets.DrawBoxSolid(new Rect(dotX, dotY, 24, 24), Color.black);
            Widgets.Label(new Rect(dotX + 5, dotY, 24, 24), (i + 1).ToString());
        }
        if (!plane.Heavy)
        {
            Widgets.Label(new Rect(16, 502, 500, 30), $"固定机炮 · 中线 · 备弹 {plane.FixedCannon.rounds}/160");
            if (Widgets.ButtonText(new Rect(550, 500, 180, 30), "补充机炮弹药")) Rearm(-1);
            TooltipHandler.TipRegion(new Rect(550, 500, 180, 30), "每20发消耗1钢铁；固定机炮不可更换或拆除。");
        }
        Widgets.Label(new Rect(16, 550, width - 8, 52), "更换挂载消耗10钢铁、1工业组件（18格内）；悬停补弹可看材料。拆副油箱会卸出余油，挂载会略降航速。");
        Widgets.Label(new Rect(16, 625, width - 8, 28), status);
    }
    public void Install(int index, StoreKind kind)
    {
        if (plane.State != FlightState.Parked) return;
        if (!plane.CanMount(index, kind)) { status = "此挂点无法安装该挂载。"; return; }
        Hardpoint m = plane.mounts[index];
        if (m.kind == kind) return;
        int seats = plane.Seats - (m.kind == StoreKind.PassengerPod ? 1 : 0) + (kind == StoreKind.PassengerPod ? 1 : 0);
        float capacity = plane.CargoCapacity - (m.kind == StoreKind.CargoPod ? 600 : m.kind == StoreKind.PassengerPod ? 100 : 0) + (kind == StoreKind.CargoPod ? 600 : kind == StoreKind.PassengerPod ? 100 : 0);
        if (plane.Cargo.innerContainer.OfType<Pawn>().Count() > seats || plane.Cargo.MassUsage > capacity) { status = "请先卸下载荷；更换后席位或货舱容量不足。"; return; }
        if (m.rounds > 0)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("拆卸会清除该挂点的剩余弹药。确认更换？", () => { m.rounds = 0; Install(index, kind); })); return;
        }
        if (kind != StoreKind.Empty && !DebugSettings.godMode)
        {
            if (!Has(ThingDefOf.Steel, 10) || !Has(ThingDefOf.ComponentIndustrial, 1)) { status = "缺少10钢铁、1组件。"; return; }
            AV.Spend(plane.Map, plane.Position, ThingDefOf.Steel, 10); AV.Spend(plane.Map, plane.Position, ThingDefOf.ComponentIndustrial, 1);
        }
        if (m.kind == StoreKind.Tank) plane.RemoveDropTankFuel();
        m.kind = kind; m.rounds = 0; plane.RefreshCapacity(); status = "挂载安装完成。";
    }
    private bool Has(ThingDef def, int n) => plane.Map.listerThings.ThingsOfDef(def)
        .Where(t => !t.IsForbidden(Faction.OfPlayer) && t.Position.InHorDistOf(plane.Position, 18)).Sum(t => t.stackCount) >= n;
    public void Rearm(int index)
    {
        if (plane.State != FlightState.Parked) { status = "请先降落。"; return; }
        if (index < 0 && plane.Heavy || index >= plane.mounts.Count) return;
        Hardpoint m = index < 0 ? plane.FixedCannon : plane.mounts[index];
        int n = (index < 0 ? 160 : plane.StoreCapacity(index, m.kind)) - m.rounds;
        if (n <= 0) return;
        if (DebugSettings.godMode) { m.rounds += n; status = "弹药补给完成。"; return; }
        var costs = Ordnance.RearmCost(m.kind, n);
        if (costs.Count == 0) return;
        if (costs.Any(c => !Has(c.def, c.count)))
        { status = "需要：" + string.Join("、", costs.Select(c => $"{c.count} {c.def.LabelCap}")); return; }
        foreach (var c in costs) AV.Spend(plane.Map, plane.Position, c.def, c.count);
        m.rounds += n; status = "弹药补给完成。";
    }
}
