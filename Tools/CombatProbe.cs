#if DEBUG
using System;
using Il2CppScheduleOne.PlayerScripts;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe for NPC aggression and combat: <c>personnel aggr &lt;id-filter&gt; [dump|pickpocket|stop]</c>.
    /// <c>dump</c> (default) logs the aggression value and the combat state. <c>pickpocket</c> runs the NPC's
    /// reaction to a failed pickpocket by the local player (vanilla <c>NPCResponses.PlayerFailedPickpocket</c>): an NPC
    /// with aggression 1 always fights back, one with aggression 0 never does. <c>stop</c> ends the fight.
    /// </summary>
    internal static class CombatProbe
    {
        internal static void Run(string filter, string action)
        {
            if (string.IsNullOrWhiteSpace(filter)) { Log("usage: personnel aggr <id-filter> [dump|pickpocket|stop]"); return; }
            S1NPC npc = CustomerProbe.Find(filter, out string id);
            if (npc == null) { Log($"aggr: no live NPC matches '{filter}'."); return; }

            switch ((action ?? "dump").ToLowerInvariant())
            {
                case "pickpocket":
                    if (!Il2CppFishNet.InstanceFinder.IsServer) { Log($"aggr pickpocket {id}: host only."); return; }
                    try { npc.Responses.PlayerFailedPickpocket(Player.Local); }
                    catch (Exception e) { Log($"aggr pickpocket {id}: threw {e.GetType().Name}: {e.Message}"); return; }
                    Dump(npc, id, "pickpocket");
                    break;
                case "stop":
                    try { npc.Behaviour.CombatBehaviour.Disable_Server(); }
                    catch (Exception e) { Log($"aggr stop {id}: threw {e.GetType().Name}: {e.Message}"); }
                    Dump(npc, id, "stop");
                    break;
                default: Dump(npc, id, "dump"); break;
            }
        }

        private static void Dump(S1NPC npc, string id, string what)
        {
            var combat = npc.Behaviour?.CombatBehaviour;
            Log($"aggr {what} {id}: aggression={CustomerProbe.Safe(() => npc.Aggression)} " +
                $"dataDefault={CustomerProbe.Safe(() => npc.NPCData.Behaviour.DefaultAggression)} " +
                $"responses={CustomerProbe.Safe(() => npc.Responses.GetIl2CppType().Name)} " +
                $"combat.enabled={CustomerProbe.Safe(() => combat.Enabled)} combat.active={CustomerProbe.Safe(() => combat.Active)} " +
                $"target={CustomerProbe.Safe(() => combat.Target != null ? combat.Target.NetworkObject.name : "none")} " +
                $"weapon={CustomerProbe.Safe(() => combat.currentWeapon != null ? combat.currentWeapon.name : "none")} " +
                $"active={CustomerProbe.Safe(() => npc.Behaviour.activeBehaviour != null ? npc.Behaviour.activeBehaviour.Name : "none")}");
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
