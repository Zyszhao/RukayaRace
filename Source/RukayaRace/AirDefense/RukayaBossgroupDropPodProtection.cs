using RimWorld;
using Verse;

namespace RukayaRace.AirDefense
{
    public static class RukayaBossgroupDropPodProtection
    {
        // 按任务中的实际乘员引用识别，不让普通袭击中的同种机械体获得保护。
        public static bool IsProtected(Skyfaller pod)
        {
            if (!(pod is DropPodIncoming) || pod.innerContainer.Count == 0)
                return false;
            QuestManager manager = Current.Game?.questManager;
            if (manager == null)
                return false;
            foreach (Quest quest in manager.ActiveQuestsListForReading)
            {
                if (quest.State != QuestState.Ongoing)
                    continue;
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (!(part is QuestPart_Bossgroup group))
                        continue;
                    foreach (Thing thing in pod.innerContainer)
                    {
                        if (IsGroupPawn(thing, group))
                            return true;
                        if (!(thing is ActiveTransporter transporter)
                            || transporter.Contents?.innerContainer == null)
                            continue;
                        foreach (Thing cargo in transporter.Contents.innerContainer)
                        {
                            if (IsGroupPawn(cargo, group))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool IsGroupPawn(Thing thing, QuestPart_Bossgroup group)
        {
            return thing is Pawn pawn && (group.pawns.Contains(pawn) || group.bosses.Contains(pawn));
        }
    }
}
