using Il2CppScheduleOne.NPCs.Schedules;
using UnityEngine;

namespace Personnel.Spawn
{
    /// <summary>
    /// Takes every schedule step out of a contact-only NPC (<c>spawn.physical: false</c>). Such an NPC has no world body,
    /// but its schedule would still run: its steps walk an invisible agent from the spawn point, and a spawn point
    /// more than 1 m from the NavMesh (inside a house, below a floor) never gets onto it. Vanilla then logs
    /// "NPC is not on navmesh" and "NavMesh sample failed" on every tick, and every walk step adds a path request.
    /// Disabling the schedule is not enough: the NPC's default <c>ScheduleBehaviour</c> enables it again on every
    /// activate and resume, so the steps themselves are made inactive, which S1API treats as unused slots.
    /// </summary>
    internal static class ContactOnlySchedule
    {
        public static void Apply(GameObject npc, string npcId)
        {
            int dropped = 0;
            foreach (NPCAction action in npc.GetComponentsInChildren<NPCAction>(true))
            {
                if (action == null || !action.gameObject.activeSelf) continue;
                action.gameObject.SetActive(false);
                dropped++;
            }
            Core.Log?.Msg($"'{npcId}': contact-only (spawn.physical false) - {dropped} schedule step(s) not run.");
        }
    }
}
