using System;
using HarmonyLib;
using Verse;

namespace RukayaRace.Weapons
{
    /// <summary>
    /// Single Harmony entry point for the whole mod. Every patch is a <c>[HarmonyPatch]</c> class and gets
    /// applied here; no other code creates a Harmony instance.
    ///
    /// <c>PatchAll</c> is wrapped because it runs from a static constructor: an unresolvable patch (a typo in a
    /// target's name, or an injected parameter such as <c>___field</c> that does not exist on the target) throws
    /// out of <c>PatchAll</c>, and an exception in a static constructor is reported as
    /// <c>TypeInitializationException</c> far away from the real cause. Catching it and logging the real
    /// exception keeps one broken patch from taking the whole mod - including its other, working patches -
    /// down with it.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RukayaHarmonyBootstrap
    {
        static RukayaHarmonyBootstrap()
        {
            try
            {
                Harmony harmony = new Harmony("zyshzao.rukayarace");
                harmony.PatchAll();
            }
            catch (Exception ex)
            {
                Log.Error("[Rukaya] Harmony PatchAll failed - some or all of this mod's patches are NOT applied, "
                          + "so the features they implement will silently do nothing.\n" + ex);
            }
        }
    }
}
