using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RukayaRace.Replication
{
    /// <summary>
    /// Slider gizmo that sets how many active matter containers a single terminal may store.
    /// The slider's leftmost position means "follow the global mod setting". Mirrors the vanilla
    /// Command_SetTargetFuelLevel flow (slider dialog + merging across a multi selection).
    /// </summary>
    // 静态字段 limitIconTexture 是 Texture2D：RimWorld 要求所有资源在主线程加载，
    // 加上该属性可让静态构造函数在启动期（主线程）就执行完。
    [StaticConstructorOnStartup]
    public class Command_SetRukayaReservoirLimit : Command
    {
        private static Texture2D limitIconTexture;
        private static bool iconResolved;

        private List<CompRukayaActiveMatterReservoir> reservoirs;

        public CompRukayaActiveMatterReservoir reservoir;

        public static Texture2D LimitIcon
        {
            get
            {
                if (!iconResolved)
                {
                    iconResolved = true;
                    limitIconTexture = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel", false);
                }

                return limitIconTexture;
            }
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            if (reservoir?.parent == null)
            {
                return;
            }

            if (reservoirs == null)
            {
                reservoirs = new List<CompRukayaActiveMatterReservoir>();
            }

            if (!reservoirs.Contains(reservoir))
            {
                reservoirs.Add(reservoir);
            }

            CompRukayaActiveMatterReservoir first = reservoirs[0];
            int globalCapacity = RukayaReservoirLoadingUtility.GlobalCapacity(first);
            int startingValue = first.UsesCustomStorageLimit ? first.StorageLimit : 0;

            // The dialog mirrors Dialog_Slider but adds a numeric entry box to the right of the slider:
            // dragging across a 0..1000 range is imprecise, and typing the exact number is what players
            // actually want when they are matching a container stockpile count.
            Find.WindowStack.Add(new Dialog_RukayaReservoirLimit(
                first.parent.LabelShortCap,
                globalCapacity,
                0,
                RukayaReplicationUtility.MaxReservoirCapacity,
                startingValue,
                delegate (int value)
                {
                    for (int i = 0; i < reservoirs.Count; i++)
                    {
                        reservoirs[i].SetStorageLimit(value);
                    }
                }));
        }

        public override bool InheritInteractionsFrom(Gizmo other)
        {
            if (other is Command_SetRukayaReservoirLimit command && command.reservoir != null)
            {
                if (reservoirs == null)
                {
                    reservoirs = new List<CompRukayaActiveMatterReservoir>();
                }

                if (!reservoirs.Contains(command.reservoir))
                {
                    reservoirs.Add(command.reservoir);
                }
            }

            return false;
        }

        public static string LimitText(int value, int globalCapacity)
        {
            return value <= 0
                ? "Rukaya_ReservoirLimitFollowGlobal".Translate(globalCapacity)
                : "Rukaya_ReservoirLimitValue".Translate(value);
        }
    }
}
