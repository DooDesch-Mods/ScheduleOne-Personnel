#if DEBUG
using System;
using System.Text;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;
using S1UnlockType = Il2CppScheduleOne.NPCs.Relation.NPCRelationData.EUnlockType;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe for relationship data of any live NPC (vanilla, Personnel or another mod):
    /// <c>personnel rel &lt;id&gt; [dump|unlock]</c>. <c>dump</c> logs unlock state, delta and the resolved connection
    /// ids in both directions, plus the vanilla sample gate (<c>IsMutuallyKnown</c>, <c>SampleOptionValid</c>) that a
    /// working connection opens. <c>unlock</c> unlocks the NPC the way a recommendation does.
    /// </summary>
    internal static class RelationProbe
    {
        internal static void Run(string filter, string action)
        {
            if (string.IsNullOrWhiteSpace(filter)) { Log("usage: personnel rel <id> [dump|unlock]"); return; }
            S1NPC npc = FindExact(filter) ?? CustomerProbe.Find(filter, out _);
            if (npc == null) { Log($"rel: no live NPC matches '{filter}'."); return; }

            if (string.Equals(action, "unlock", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    npc.RelationData.Unlock(S1UnlockType.Recommendation, true);
                    Log($"rel unlock {npc.ID}: unlocked={CustomerProbe.Safe(() => npc.RelationData.Unlocked)}");
                }
                catch (Exception e) { Log($"rel unlock {npc.ID}: Unlock threw {e.GetType().Name}: {e.Message}"); }
                return;
            }
            Dump(npc);
        }

        private static void Dump(S1NPC npc)
        {
            var rd = npc.RelationData;
            if (rd == null) { Log($"rel {npc.ID}: RelationData MISSING"); return; }

            var sb = new StringBuilder();
            sb.Append($"rel {npc.ID}: unlocked={CustomerProbe.Safe(() => rd.Unlocked)} type={CustomerProbe.Safe(() => rd.UnlockType)} ")
              .Append($"delta={CustomerProbe.Safe(() => rd.RelationDelta)} mutuallyKnown={CustomerProbe.Safe(() => rd.IsMutuallyKnown())} connections=[");
            try
            {
                var list = rd.Connections;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    S1NPC c = list[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(c == null ? "<null>" : c.ID + (CustomerProbe.Safe(() => c.RelationData.Unlocked) == "True" ? "*" : ""));
                }
            }
            catch (Exception e) { sb.Append("ERR(" + e.GetType().Name + ")"); }
            sb.Append("]");

            // Reverse direction: who lists this NPC as a connection.
            sb.Append(" listedBy=[");
            int n = 0;
            foreach (var other in Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry)
            {
                try
                {
                    var l = other?.RelationData?.Connections;
                    if (l == null) continue;
                    foreach (var c in l)
                        if (c != null && c.ID == npc.ID) { sb.Append(n++ > 0 ? ", " : "").Append(other.ID); break; }
                }
                catch (Exception e) { Log($"rel {npc.ID}: reading connections of {other?.ID} threw {e.GetType().Name}"); }
            }
            sb.Append("]");

            S1Customer cust = npc.GetComponent<S1Customer>();
            if (cust != null)
                sb.Append(" sampleValid=").Append(CustomerProbe.Safe(() => { bool ok = cust.SampleOptionValid(out string why); return ok + (ok ? "" : "(" + why + ")"); }));
            Log(sb.ToString());
        }

        /// <summary><c>personnel line &lt;id&gt; &lt;Module&gt; &lt;key&gt;</c>: logs five draws of a line the way the game reads it.</summary>
        internal static void Line(string filter, string module, string key)
        {
            if (string.IsNullOrWhiteSpace(filter) || string.IsNullOrWhiteSpace(key)) { Log("usage: personnel line <id> <Module> <key>"); return; }
            S1NPC npc = FindExact(filter) ?? CustomerProbe.Find(filter, out _);
            if (npc == null) { Log($"line: no live NPC matches '{filter}'."); return; }
            if (!Enum.TryParse(module, true, out Il2CppScheduleOne.Dialogue.EDialogueModule type)) { Log($"line: unknown module '{module}'."); return; }
            var h = npc.GetComponentInChildren<Il2CppScheduleOne.Dialogue.DialogueHandler>(true);
            if (h?.Database == null) { Log($"line {npc.ID}: no dialogue database."); return; }
            var sb = new StringBuilder($"line {npc.ID} {type}/{key}:");
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    var chain = h.Database.GetChain(type, key);
                    sb.Append(" [").Append(chain?.Lines == null ? "<none>" : string.Join(" | ", chain.Lines)).Append(']');
                }
                catch (Exception e) { sb.Append(" ERR(" + e.GetType().Name + ")"); }
            }
            Log(sb.ToString());
        }

        private static S1NPC FindExact(string id)
        {
            foreach (var n in Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry)
                if (n != null && string.Equals(n.ID, id, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
