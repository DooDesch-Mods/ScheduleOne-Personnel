#if DEBUG
using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe for NPC aggression and combat: <c>personnel aggr &lt;id-filter&gt; [dump|pickpocket|face|stop]</c>.
    /// <c>dump</c> (default) logs the aggression value and the combat state. <c>pickpocket</c> runs the NPC's
    /// reaction to a failed pickpocket by the local player (vanilla <c>NPCResponses.PlayerFailedPickpocket</c>): an NPC
    /// with aggression 1 always fights back, one with aggression 0 never does. <c>face</c> puts the local player 5 m in front of the NPC, looking at it.
    /// <c>stop</c> ends the fight.
    /// </summary>
    internal static class CombatProbe
    {
        internal static void Run(string filter, string action)
        {
            if (string.IsNullOrWhiteSpace(filter)) { Log("usage: personnel aggr <id-filter> [dump|pickpocket|face|stop]"); return; }
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
                case "face":
                    Face(npc, id);
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
                $"defaultWeapon={CustomerProbe.Safe(() => combat._defaultWeapon != null ? combat._defaultWeapon.AssetPath : "fists")} " +
                $"active={CustomerProbe.Safe(() => npc.Behaviour.activeBehaviour != null ? npc.Behaviour.activeBehaviour.Name : "none")} " +
                $"cone.enabled={CustomerProbe.Safe(() => npc.Awareness.VisionCone.enabled)} " +
                $"player.health={CustomerProbe.Safe(() => Player.Local.Health.CurrentHealth)} player.alive={CustomerProbe.Safe(() => Player.Local.Health.IsAlive)} " +
                $"player.unconscious={CustomerProbe.Safe(() => Player.Local.IsUnconscious)} " +
                $"seesLocal={CustomerProbe.Safe(() => npc.Awareness.VisionCone.IsPlayerVisible(Player.Local))} " +
                $"inCone={CustomerProbe.Safe(() => npc.Awareness.VisionCone.IsPointWithinSight(Player.Local.Visibility.CenterPoint, true, null))} " +
                $"inConeLoS={CustomerProbe.Safe(() => npc.Awareness.VisionCone.IsPointWithinSight(Player.Local.Visibility.CenterPoint, false, null))} " +
                $"playerVisibility={CustomerProbe.Safe(() => Player.Local.Visibility.CurrentVisibility)} " +
                $"ofInterest={CustomerProbe.Safe(() => npc.Awareness.VisionCone.sightablesOfInterest.Count)} " +
                $"range={CustomerProbe.Safe(() => npc.Awareness.VisionCone.effectiveRange)} " +
                $"originFwd={CustomerProbe.Safe(() => npc.Awareness.VisionCone.VisionOrigin.forward)} " +
                $"toPlayer={CustomerProbe.Safe(() => (Player.Local.Visibility.CenterPoint - npc.Awareness.VisionCone.VisionOrigin.position).normalized)}");
        }

        // Puts the local player 5 m in front of the NPC, looking at it, so the NPC's vision cone sees the player.
        private static void Face(S1NPC npc, string id)
        {
            var mv = PlayerSingleton<PlayerMovement>.Instance;
            if (mv == null || Player.Local == null) { Log($"aggr face {id}: no local player."); return; }
            Transform t = npc.transform;
            Vector3 spot = t.position + t.forward * 5f + Vector3.up * 0.1f;
            Vector3 look = t.position - spot;
            look.y = 0f;
            try { mv.Teleport(spot, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity, false); }
            catch (Exception e) { Log($"aggr face {id}: teleport threw {e.GetType().Name}: {e.Message}"); return; }
            // A test fight must not knock the player out before the checks are done.
            var health = Player.Local.Health;
            if (health != null && health.IsAlive) health.SetHealth(Il2CppScheduleOne.PlayerScripts.Health.PlayerHealth.MaxHealth);
            Log($"aggr face {id}: player at {spot}, npc at {t.position}, player health {CustomerProbe.Safe(() => health.CurrentHealth)} alive {CustomerProbe.Safe(() => health.IsAlive)}.");
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
