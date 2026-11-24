using System;
using System.Collections.Generic;
using System.Xml;
using HarmonyLib;
using Verse;

namespace RukayaAviation;

// Convert the in-memory save tree before any defs, Thing IDs or cross-references
// are read. The original save file is untouched; subsequent saves use the new names.
[HarmonyPatch(typeof(ScribeLoader), nameof(ScribeLoader.InitLoading))]
public static class AviationSaveCompatibility
{
    public static string ConvertReference(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        char first = value[0];
        if (first != 'R' && first != 'T') return value;
        if (first == 'T' && value.StartsWith("Thing_", StringComparison.Ordinal))
        {
            string original = value.Substring("Thing_".Length);
            string renamed = ConvertReference(original);
            return renamed == original ? value : "Thing_" + renamed;
        }
        if (first != 'R') return value;
        if (value == "Rukaya_Mig31") return "Rukaya_Mig31Research";
        string converted = ConvertThingReference(value, "Rukaya_CoreMemory_Mig31", "Rukaya_CoreMemory_Mig31Memory");
        return ConvertThingReference(converted, "Rukaya_Archive_Mig31", "Rukaya_Archive_Mig31Site");
    }

    private static string ConvertThingReference(string value, string oldName, string newName)
    {
        if (value == oldName) return newName;
        // Thing load IDs append the instance number directly to the DefName.
        // Replace that prefix on both the object and all references to it.
        if (value == null || !value.StartsWith(oldName, StringComparison.Ordinal)) return value;
        for (int i = oldName.Length; i < value.Length; i++)
            if (value[i] < '0' || value[i] > '9') return value;
        return newName + value.Substring(oldName.Length);
    }

    public static void Postfix(ScribeLoader __instance)
    {
        if (Scribe.mode != LoadSaveMode.LoadingVars || __instance.curXmlParent == null) return;
        var pending = new Stack<XmlNode>();
        pending.Push(__instance.curXmlParent);
        while (pending.Count > 0)
        {
            XmlNode node = pending.Pop();
            if (node.NodeType == XmlNodeType.Text)
            {
                string converted = ConvertReference(node.Value);
                if (converted != node.Value) node.Value = converted;
            }
            else
            {
                foreach (XmlNode child in node.ChildNodes) pending.Push(child);
            }
        }
    }
}
