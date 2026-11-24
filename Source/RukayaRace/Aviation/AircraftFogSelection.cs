using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaAviation;

// Native selection explicitly rejects fully fogged rectangles. Supplement only
// owned aircraft; neither reveal terrain nor expose enemy units under the fog.
public class AircraftFogSelection : MapComponent
{
    private bool dragging, additive;
    private Vector3 start;
    private Vector2 screenStart;
    private List<Aircraft> pending;
    private bool pendingClear;
    public AircraftFogSelection(Map map) : base(map) { }
    public IEnumerable<Aircraft> InRect(CellRect rect) => AV.Planes(map).Where(p => p.Faction == Faction.OfPlayer
        && !ThingSelectionUtility.SelectableByMapClick(p) && p.CustomRectForSelector.Value.Overlaps(rect));
    public override void MapComponentOnGUI()
    {
        if (Find.CurrentMap != map || Find.World.renderer.wantedMode != RimWorld.Planet.WorldRenderMode.None) return;
        Event e = Event.current;
        if (e == null) return;
        EventType type = e.rawType;
        if (type != EventType.MouseDown && type != EventType.MouseUp) return;
        if (Find.Targeter.IsTargeting || Find.DesignatorManager.SelectedDesignator != null || Mouse.IsInputBlockedNow)
        { dragging = false; return; }
        HandleMapInput(e, UI.MouseMapPosition());
    }
    public void HandleMapInput(Event e, Vector3 mouseMapPosition)
    {
        if (e.rawType == EventType.MouseDown && e.button == 1 && !Find.Selector.SelectedPawns.Any())
        {
            Aircraft[] planes = Find.Selector.SelectedObjects.OfType<Aircraft>()
                .Where(p => p.Faction == Faction.OfPlayer && p.Map == map && p.State == FlightState.Flying).ToArray();
            if (planes.Length > 0)
            {
                foreach (Aircraft plane in planes) plane.OrderMove(mouseMapPosition.ToIntVec3());
                if (AV.SafeAir(mouseMapPosition.ToIntVec3(), map)) FleckMaker.Static(mouseMapPosition, map, FleckDefOf.FeedbackGoto);
                e.Use(); return;
            }
        }
        if (e.rawType == EventType.MouseDown && e.button == 0)
        { dragging = true; start = mouseMapPosition; screenStart = e.mousePosition; additive = e.shift; }
        if (e.rawType != EventType.MouseUp || e.button != 0 || !dragging) return;
        dragging = false;
        Vector3 end = mouseMapPosition; bool box = Vector2.Distance(screenStart, e.mousePosition) > 8;
        CellRect rect = box ? CellRect.FromLimits(start.ToIntVec3(), end.ToIntVec3()) : CellRect.SingleCell(end.ToIntVec3());
        pending = InRect(rect).ToList(); pendingClear = !box && !additive;
        // Run after vanilla processes this same mouse-up, retaining its ordinary
        // drag selection and Shift behavior while adding the fogged aircraft.
    }
    public override void MapComponentUpdate()
    {
        if (pending == null) return;
        if (pending.Count > 0)
        {
            if (pendingClear) Find.Selector.ClearSelection();
            foreach (Aircraft plane in pending.Where(p => p.Spawned && !p.Destroyed)) Find.Selector.Select(plane);
        }
        pending = null;
    }
}
