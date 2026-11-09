using System;
using Personnel.Model;
using UnityEngine;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Spawn
{
    /// <summary>
    /// Applies <c>behavior.aggression</c> to the game NPC. The game's <c>NPC.Aggression</c> is a getter over
    /// <c>AggressionController.Value</c>, so it cannot be written directly (S1API's <c>Aggressiveness</c> setter tries
    /// that and fails without effect). The value goes into the controller's default, and into
    /// <c>NPCData.Behaviour.DefaultAggression</c> so a later <c>InitializeNPC</c> applies the same value. Temporary
    /// effects such as meth stay on top of it, as for vanilla NPCs.
    /// </summary>
    internal static class AggressionSetter
    {
        internal static void Apply(GameObject go, NpcDef def)
        {
            float value = Mathf.Clamp01(def.Behavior.Aggression);
            if (!Mathf.Approximately(value, def.Behavior.Aggression))
                Core.Log?.Warning($"'{def.Id}': behavior.aggression {def.Behavior.Aggression} is outside 0..1 - using {value}.");

            S1NPC npc = go != null ? go.GetComponent<S1NPC>() : null;
            if (npc == null) { Core.Log?.Error($"'{def.Id}': no game NPC on the object - aggression {value} not applied."); return; }

            try
            {
                var data = npc.NPCData;
                if (data != null && data.Behaviour != null) data.Behaviour.DefaultAggression = value;
            }
            catch (Exception ex) { Core.Log?.Warning($"'{def.Id}': writing the NPC data's default aggression failed ({ex.Message})."); }

            try { npc.AggressionController.SetDefaultValue(value); }
            catch (Exception ex) { Core.Log?.Error($"'{def.Id}': setting aggression {value} failed ({ex.Message})."); return; }

            float now = npc.Aggression;
            if (Mathf.Approximately(now, value)) Core.Log?.Msg($"'{def.Id}': aggression {value}.");
            else Core.Log?.Warning($"'{def.Id}': aggression set to {value} but reads {now} (a temporary effect is on top).");
        }
    }
}
