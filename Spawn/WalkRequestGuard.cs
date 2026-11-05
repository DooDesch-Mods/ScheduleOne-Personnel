using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using UnityEngine;
using UnityEngine.AI;

namespace Personnel.Spawn
{
    /// <summary>
    /// Keeps a Personnel NPC from cancelling its own path request every frame.
    /// </summary>
    /// <remarks>
    /// Schedule steps (walk to, location dialogue, location action) ask for their path again in every
    /// <c>ActiveUpdate</c> while <c>NPCMovement.IsMoving</c> is false. <c>IsMoving</c> is also false while the agent
    /// still waits for its path (<c>pathPending</c> with no path yet). <c>SetDestination</c> then ends the pending
    /// request as <c>Interrupted</c>, the step logs "NPC walk to location not successful", and the same path goes
    /// back into the NavMesh pathfinding queue. A path that takes longer than one frame never finishes, and with
    /// many NPCs doing this the shared queue stays full, so other NPCs (employees, vanilla townsfolk) wait for their
    /// paths too. A fresh NPC has no cached paths, so every Personnel NPC takes the queued route.
    /// <para>
    /// The patch drops a request for the same destination while the previous one is still pending, and hands the
    /// new callback to the pending request so the step that asked gets the result. Only NPCs Personnel spawned are
    /// affected; a request for another destination, or from an NPC whose path has already been computed, runs as
    /// before.
    /// </para>
    /// </remarks>
    internal static class WalkRequestGuard
    {
        // Same destination within 10 cm; CurrentDestination is the position the caller passed, not a snapped one.
        private const float SameDestinationSqr = 0.01f;
        private const float SummaryIntervalSeconds = 300f;

        private static readonly Dictionary<int, string> Watched = new Dictionary<int, string>();
        private static readonly Dictionary<string, int> KeptPerNpc = new Dictionary<string, int>();
        private static int _keptSinceSummary;
        private static float _nextSummaryAt = -1f;

        internal static void Watch(GameObject npcObject, string id)
        {
            if (npcObject == null) { Core.Log?.Warning($"'{id}': no NPC object - path request guard skipped."); return; }
            NPC npc = npcObject.GetComponent<NPC>();
            if (npc == null) npc = npcObject.GetComponentInChildren<NPC>(true);
            if (npc == null || npc.Movement == null) { Core.Log?.Warning($"'{id}': no NPCMovement found - path request guard skipped."); return; }
            Watched[npc.Movement.GetInstanceID()] = id;
        }

        [HarmonyPatch(typeof(NPCMovement), nameof(NPCMovement.SetDestination),
            new[] { typeof(Vector3), typeof(Il2CppSystem.Action<NPCMovement.WalkResult>), typeof(bool), typeof(float), typeof(float) })]
        private static class SetDestinationPatch
        {
            private static bool Prefix(NPCMovement __instance, Vector3 pos, Il2CppSystem.Action<NPCMovement.WalkResult> callback,
                bool interruptExistingCallback)
            {
                try
                {
                    if (!interruptExistingCallback || __instance == null || !__instance._hasDestination) return true;
                    if (!Watched.TryGetValue(__instance.GetInstanceID(), out string id)) return true;
                    NavMeshAgent agent = __instance._agent;
                    if (agent == null) return true;
                    if ((__instance.CurrentDestination - pos).sqrMagnitude > SameDestinationSqr) return true;
                    if (!agent.pathPending)
                    {
#if DEBUG
                        CountPassed(id, pos, agent);
#endif
                        return true;
                    }

                    __instance._moveResultCallback = callback;
                    Count(id, pos, agent);
                    return false;
                }
                catch (Exception ex)
                {
                    Core.Log?.Error($"Path request guard failed, the request runs unchanged ({ex.Message}).");
                    return true;
                }
            }
        }

        private static void Count(string id, Vector3 pos, NavMeshAgent agent)
        {
            KeptPerNpc.TryGetValue(id, out int n);
            KeptPerNpc[id] = ++n;
            _keptSinceSummary++;
#if DEBUG
            if (n == 1 || n % 1000 == 0)
                Core.Log?.Msg($"[dev] '{id}': kept pending path request x{n} to {pos} " +
                              $"(hasPath {agent.hasPath}, remainingDistance {agent.remainingDistance:F1}, isOnNavMesh {agent.isOnNavMesh}).");
#endif
            float now = Time.realtimeSinceStartup;
            if (_nextSummaryAt < 0f) { _nextSummaryAt = now + SummaryIntervalSeconds; return; }
            if (now < _nextSummaryAt) return;
            Core.Log?.Msg($"Path requests: kept {_keptSinceSummary} repeat request(s) from {KeptPerNpc.Count} NPC(s) " +
                          $"waiting for their path in the last {SummaryIntervalSeconds / 60f:F0} min.");
            _keptSinceSummary = 0;
            KeptPerNpc.Clear();
            _nextSummaryAt = now + SummaryIntervalSeconds;
        }

#if DEBUG
        private static readonly Dictionary<string, int> PassedPerNpc = new Dictionary<string, int>();

        // A repeat request for the same destination with no path pending: the case this guard leaves alone. Counted so
        // the log shows whether repeats still interrupt walks for another reason.
        private static void CountPassed(string id, Vector3 pos, NavMeshAgent agent)
        {
            PassedPerNpc.TryGetValue(id, out int n);
            PassedPerNpc[id] = ++n;
            if (n == 1 || n % 1000 == 0)
                Core.Log?.Msg($"[dev] '{id}': repeat path request x{n} to {pos} with no path pending " +
                              $"(hasPath {agent.hasPath}, status {agent.pathStatus}, remainingDistance {agent.remainingDistance:F1}).");
        }

        // Every "NPC walk to location not successful" of a Personnel NPC, counted per NPC, result and destination and
        // written to this game's MelonLoader log every 30 s. Player.log, where the vanilla warning goes, is shared by
        // every running game, so tests read these lines instead.
        private const float WalkReportSeconds = 30f;
        private static readonly Dictionary<string, int> FailedWalks = new Dictionary<string, int>();
        private static int _failedResultsSinceReport;
        private static float _nextWalkReportAt = -1f;

        [HarmonyPatch(typeof(Il2CppScheduleOne.NPCs.Schedules.NPCSignal_WalkToLocation),
            nameof(Il2CppScheduleOne.NPCs.Schedules.NPCSignal_WalkToLocation.WalkCallback))]
        private static class WalkCallbackPatch
        {
            private static void Prefix(Il2CppScheduleOne.NPCs.Schedules.NPCSignal_WalkToLocation __instance, NPCMovement.WalkResult result)
            {
                try
                {
                    if (result == NPCMovement.WalkResult.Success || __instance == null || !__instance.IsActive) return;
                    NPC npc = __instance.npc;
                    if (npc == null || npc.Movement == null) return;
                    if (!Watched.TryGetValue(npc.Movement.GetInstanceID(), out string id)) return;
                    Vector3 to = __instance.Destination != null ? __instance.Destination.position : Vector3.zero;
                    float gap = Vector3.Distance(npc.Movement.FootPosition, to);
                    string key = $"'{id}' {result} to ({to.x:F2}, {to.y:F2}, {to.z:F2}), {gap:F1} m away";
                    FailedWalks.TryGetValue(key, out int n);
                    FailedWalks[key] = n + 1;
                    _failedResultsSinceReport++;
                }
                catch (Exception ex)
                {
                    Core.Log?.Error($"[dev] walk result count failed ({ex.Message}).");
                }
            }
        }

        /// <summary>Writes the failed walk counts of the last 30 s. Called every frame from Core.OnUpdate.</summary>
        internal static void ReportWalkResults()
        {
            float now = Time.realtimeSinceStartup;
            if (_nextWalkReportAt < 0f) { _nextWalkReportAt = now + WalkReportSeconds; return; }
            if (now < _nextWalkReportAt) return;
            _nextWalkReportAt = now + WalkReportSeconds;
            var byResult = new Dictionary<string, int>();
            foreach (var kv in FailedWalks)
            {
                string result = kv.Key.Split(' ')[1];
                byResult.TryGetValue(result, out int r);
                byResult[result] = r + kv.Value;
            }
            var parts = new List<string>();
            foreach (var kv in byResult) parts.Add($"{kv.Key} {kv.Value}");
            Core.Log?.Msg($"[dev] walk results ({WalkReportSeconds:F0} s): {_failedResultsSinceReport} not successful" +
                          (parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "") + $" from {FailedWalks.Count} NPC/destination pair(s).");
            var top = new List<KeyValuePair<string, int>>(FailedWalks);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < top.Count && i < 8; i++)
                Core.Log?.Msg($"[dev]   walk x{top[i].Value} {top[i].Key}");
            FailedWalks.Clear();
            _failedResultsSinceReport = 0;
        }
#endif
    }
}
