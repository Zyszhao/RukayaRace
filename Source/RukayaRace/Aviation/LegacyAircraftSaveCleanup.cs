using System.Collections.Generic;
using System.Xml;
using HarmonyLib;
using Verse;

namespace RukayaAviation;

// The standalone demo serialized every GameComponent, even in normal games.
// Discard only the removed test components before vanilla resolves their types.
// Keep aircraft, names, maps, cargo and pending air raids untouched.
[HarmonyPatch(typeof(Game), "ExposeSmallComponents")]
internal static class LegacyAircraftSaveCleanup
{
    private static readonly HashSet<string> RemovedComponents = new()
    {
        "RukayaAviation.AirDefenseSmoke", "RukayaAviation.AirframeSmoke",
        "RukayaAviation.AirportServiceSmoke", "RukayaAviation.AirRaidDefenseSmoke",
        "RukayaAviation.AviationRegression", "RukayaAviation.CloseRouteSmoke",
        "RukayaAviation.DemoInspection", "RukayaAviation.FlightHandlingSmoke",
        "RukayaAviation.FlightSmoke", "RukayaAviation.LoadoutCombatSmoke",
        "RukayaAviation.OperationsSmoke", "RukayaAviation.OrdnanceSmoke"
    };

    private static void Prefix()
    {
        if (Scribe.mode != LoadSaveMode.LoadingVars) return;
        XmlNode components = Scribe.loader.curXmlParent?["components"];
        if (components == null) return;
        for (int i = components.ChildNodes.Count - 1; i >= 0; i--)
        {
            XmlNode child = components.ChildNodes[i];
            string type = child.Attributes?["Class"]?.Value;
            if (type != null && RemovedComponents.Contains(type)) components.RemoveChild(child);
        }
    }
}
