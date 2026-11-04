using HarmonyLib;
using Verse;
using Verse.AI;

namespace RukayaRace.Flight
{
    [HarmonyPatch(typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage))]
    public static class Patch_ThinkNode_JobGiver_RukayaFlightAI
    {
        public static void Postfix(Pawn pawn, ref ThinkResult __result)
        {
            if (__result.FromQueue || (__result.Job != null && __result.Job == pawn?.CurJob))
            {
                return;
            }

            Job flight = RukayaFlightAIUtility.TryMakeEmergencyFlightJob(
                pawn, __result.Job, __result.SourceNode);
            if (flight == null)
            {
                return;
            }

            Job movement = __result.Job;
            __result = new ThinkResult(flight, __result.SourceNode, __result.Tag, __result.FromQueue);
            JobMaker.ReturnToPool(movement);
        }
    }
}
