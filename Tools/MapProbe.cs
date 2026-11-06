#if DEBUG
using System;
using UnityEngine;
using S1NPCPoI = Il2CppScheduleOne.Map.NPCPoI;
using S1POI = Il2CppScheduleOne.Map.POI;

namespace Personnel.Tools
{
    /// <summary>
    /// <c>personnel map [show]</c>: lists every NPC map marker with the text its label carries, for test scripts.
    /// <c>show</c> switches the labels of all NPC markers to always visible (the game shows them only on hover),
    /// so a screenshot of the phone map shows every name at once. Every result is one <c>[map-probe]</c> line.
    /// </summary>
    internal static class MapProbe
    {
        private const string VanillaDefault = "PoI Main Text";

        public static void Run(string sub)
        {
            bool show = string.Equals(sub, "show", StringComparison.OrdinalIgnoreCase);
            var all = UnityEngine.Object.FindObjectsOfType<S1NPCPoI>();
            int total = 0, unnamed = 0, shown = 0;
            foreach (var poi in all)
            {
                if (poi == null) continue;
                total++;
                string text = poi.MainText ?? "";
                bool isDefault = text.Length == 0 || text == VanillaDefault;
                if (isDefault) unnamed++;
                string npc = poi.NPC != null ? poi.NPC.ID : "(none)";
                Line($"marker npc={npc} text='{text.Replace("\n", "\\n")}' default={isDefault} ui={poi.UI != null}");

                if (!show) continue;
                poi.MainTextVisibility = S1POI.TextShowMode.Always;
                var label = poi.UI != null ? poi.UI.Find("MainLabel") : null;
                if (label == null) { Line($"show npc={npc} skipped: no MainLabel (map UI not built yet)"); continue; }
                // The label is a uGUI Text; this project has no UnityEngine.UI reference, so it is enabled as a Behaviour.
                foreach (var b in label.GetComponents<Behaviour>()) b.enabled = true;
                shown++;
            }
            Line($"summary markers={total} unnamed={unnamed}" + (show ? $" shown={shown}" : ""));
        }

        private static void Line(string s) => Core.Log?.Msg("[map-probe] " + s);
    }
}
#endif
