using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RukayaRace.Replication
{
    public class Building_RukayaTriplexScanner : Building_RukayaReplicationBase
    {
        public bool TryScan(Thing source, out string reason)
        {
            reason = null;
            if (!CanOperate)
            {
                reason = "Rukaya_ReplicationBuildingUnavailable".Translate();
                return false;
            }

            if (!RukayaReplicationUtility.IsEligibleThing(source) || source.MapHeld != Map)
            {
                reason = "Rukaya_ScannerInvalidTarget".Translate();
                return false;
            }

            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            ThingDef templateDef = RukayaReplicationUtility.PrototypeDef(source.def);
            QualityCategory scannedQuality = RukayaReplicationUtility.GetQuality(source);
            // Even sub-normal prototypes unlock normal-quality printing and all cheaper tiers.
            QualityCategory desiredQuality = RukayaReplicationUtility.ClampTemplateQuality(scannedQuality);
            if (library == null || (library.IsUnlocked(templateDef) && library.GetMaxQuality(templateDef) >= desiredQuality))
            {
                reason = "Rukaya_ScannerNoUpgrade".Translate();
                return false;
            }

            int cost = RukayaReplicationUtility.GetScanCost(source);
            if (Reservoir == null || Reservoir.StoredContainers < cost)
            {
                reason = "Rukaya_ReservoirInsufficient".Translate(cost);
                return false;
            }

            if (!Reservoir.TryConsume(cost))
            {
                reason = "Rukaya_ReservoirInsufficient".Translate(cost);
                return false;
            }

            library.UnlockOrUpgrade(templateDef, desiredQuality);
            reason = "Rukaya_ScannerSuccess".Translate(templateDef.LabelCap, RukayaReplicationUtility.QualityLabel(desiredQuality));
            return true;
        }

        public bool TryUpgradeQuality(ThingDef def, out string reason)
        {
            reason = null;
            GameComponent_RukayaReplicationLibrary library = RukayaReplicationUtility.Library;
            if (!CanOperate)
            {
                reason = "Rukaya_ReplicationBuildingUnavailable".Translate();
                return false;
            }

            if (library == null || !library.IsUnlocked(def))
            {
                reason = "Rukaya_ScannerTemplateLocked".Translate();
                return false;
            }

            QualityCategory current = library.GetMaxQuality(def);
            if (current >= RukayaReplicationUtility.MaxPrintableQuality)
            {
                reason = "Rukaya_ScannerQualityMaximum".Translate();
                return false;
            }

            QualityCategory next = current + 1;
            int cost = RukayaReplicationUtility.GetQualityUpgradeCost(current, next);
            if (Reservoir == null || Reservoir.StoredContainers < cost || !Reservoir.TryConsume(cost))
            {
                reason = "Rukaya_ReservoirInsufficient".Translate(cost);
                return false;
            }

            library.UnlockOrUpgrade(def, next);
            reason = "Rukaya_ScannerUpgradeSuccess".Translate(def.LabelCap, RukayaReplicationUtility.QualityLabel(next));
            return true;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "Rukaya_OpenScanner".Translate(),
                defaultDesc = "Rukaya_OpenScannerDesc".Translate(),
                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/LaunchReport"),
                action = delegate { Find.WindowStack.Add(new Window_RukayaScanner(this)); }
            };
            if (!CanOperate)
            {
                command.Disable("Rukaya_ReplicationBuildingUnavailable".Translate());
            }

            yield return command;
        }
    }
}
