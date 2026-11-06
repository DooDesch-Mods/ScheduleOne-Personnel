using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using Personnel.Model;
using UnityEngine;
using NPCManager = Il2CppScheduleOne.NPCs.NPCManager;
using NPCRelationData = Il2CppScheduleOne.NPCs.Relation.NPCRelationData;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Spawn
{
    /// <summary>
    /// Unlocks a pack customer that starts locked but has no connection in the game. A customer with
    /// relationships.connections starts locked (<see cref="API.ResolveCustomerUnlocked"/>), and vanilla opens a locked
    /// customer only through a free sample or a recommendation, both of which need an unlocked connection
    /// (Customer.SampleOptionValid -> NPCRelationData.IsMutuallyKnown). The pack only names connection ids; when none of
    /// them is an NPC in this game (another pack not installed, a typo) the vanilla relation list stays empty and the
    /// customer could never be opened.
    /// S1API spawns mod NPCs and fills their relation list after LoadManager reports the save as loaded, so the check
    /// runs per NPC from its OnCreated, once the save is loaded and the NPC has settled. Unlock state is not networked
    /// here: every peer has the same packs and applies the same rule on its own load, the same way region start NPCs
    /// are unlocked (MapRegionData.SetUnlocked).
    /// </summary>
    internal static class ConnectionUnlockGuard
    {
        /// <summary>Seconds after the save is loaded and the NPC exists before its relation list is read.</summary>
        private const float SettleSeconds = 2f;

        /// <summary>Queues the check for one spawned pack NPC; only customers with connections are checked.</summary>
        internal static void Watch(GameObject npc, NpcDef def)
        {
            if (npc == null || def == null) return;
            if (API.ResolveRole(def) != "customer") return;
            List<string> ids = def.Relationships?.Connections;
            if (ids == null || ids.Count == 0) return;
            MelonCoroutines.Start(CheckWhenSettled(npc, def, ids));
        }

        private static IEnumerator CheckWhenSettled(GameObject go, NpcDef def, List<string> ids)
        {
            float until = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < until && !(LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded))
                yield return new WaitForSeconds(0.5f);
            if (!(LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded))
            {
                Core.Log?.Warning($"[unlock] '{def.Id}': the save did not finish loading within 180 s - its connections were not checked.");
                yield break;
            }
            // An empty list is read again a few times before it counts: S1API may still be resolving the ids.
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                yield return new WaitForSeconds(SettleSeconds);
                if (go == null) { Core.Log?.Msg($"[unlock] '{def.Id}': gone before its connections were checked - skipped."); yield break; }
                bool done;
                try { done = Check(go, def, ids, attempt == Attempts); }
                catch (Exception ex)
                {
                    Core.Log?.Error($"[unlock] '{def.Id}': checking its connections failed ({ex.GetType().Name}: {ex.Message}).");
                    yield break;
                }
                if (done) yield break;
            }
        }

        /// <summary>How often an empty relation list is read before the customer counts as unreachable.</summary>
        private const int Attempts = 4;

        /// <summary>Returns false when the list is still empty and <paramref name="final"/> is not set yet.</summary>
        private static bool Check(GameObject go, NpcDef def, List<string> ids, bool final)
        {
            string name = string.IsNullOrWhiteSpace(def.DisplayName) ? def.Id : def.DisplayName;
            S1NPC npc = go.GetComponent<S1NPC>();
            if (npc == null) { Core.Log?.Warning($"[unlock] '{name}' ({def.Id}): no NPC component - skipped."); return true; }
            NPCRelationData rel = npc.RelationData;
            if (rel == null) { Core.Log?.Warning($"[unlock] '{name}' ({def.Id}): no relation data - skipped."); return true; }
            if (rel.Unlocked) { Core.Log?.Msg($"[unlock] '{name}' ({def.Id}): already unlocked."); return true; }

            int live = 0;
            var list = rel.Connections;
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) live++;
            if (live > 0)
            {
                Core.Log?.Msg($"[unlock] '{name}' ({def.Id}): {live} connection(s) in the game - stays locked until a sample or a recommendation.");
                return true;
            }
            if (!final) return false;

            var missing = new List<string>();
            var dropped = new List<string>();
            foreach (string raw in ids)
            {
                string id = raw?.Trim();
                if (string.IsNullOrEmpty(id)) continue;
                if (Find(id) == null) missing.Add(id); else dropped.Add(id);
            }
            string why = missing.Count > 0 ? $"no NPC with the id(s) {string.Join(", ", missing)} is in the game" : "";
            if (dropped.Count > 0)
                why += (why.Length > 0 ? "; " : "") + $"the game dropped {string.Join(", ", dropped)} (another region or the NPC itself)";

            if (def.Relationships?.Unlocked == false)
            {
                Core.Log?.Warning($"[unlock] '{name}' ({def.Id}, pack '{def.Source}') is locked by relationships.unlocked=false and has no connection in the game ({why}) - it can never be unlocked by a sample or a recommendation. Left locked as the pack asks.");
                return true;
            }

            rel.Unlock(NPCRelationData.EUnlockType.DirectApproach, false);
            Core.Log?.Warning($"[unlock] '{name}' ({def.Id}, pack '{def.Source}') had no connection in the game ({why}) - unlocked, since a locked customer is only opened through a connection.");
            return true;
        }

        private static S1NPC Find(string id)
        {
            try { return NPCManager.GetNPC(id); }
            catch (Exception e) { Core.Log?.Warning($"[unlock] looking up '{id}' failed ({e.GetType().Name}: {e.Message})."); return null; }
        }
    }
}
