using UnityEngine;
using Verse;

namespace RukayaRace.Research
{
    // Static appearance only: no turret AI, weapon, ammunition or interception components.
    public class Building_RukayaAbandonedNaval : Building
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            string path = "Rukaya/Building/Rukaya_NavalArtillery/Rukaya_NavalArtillery_turret_";
            GraphicDatabase.Get<Graphic_Single>(path + "barrel", ShaderDatabase.Cutout, def.graphicData.drawSize, Color.white)
                .Draw(drawLoc + Altitudes.AltIncVect * 0.5f, Rot4.North, this);
            GraphicDatabase.Get<Graphic_Single>(path + "top", ShaderDatabase.Cutout, def.graphicData.drawSize, Color.white)
                .Draw(drawLoc + Altitudes.AltIncVect, Rot4.North, this);
        }
    }
}
