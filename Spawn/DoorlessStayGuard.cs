using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Schedules;
using UnityEngine;

namespace Personnel.Spawn
{
    /// <summary>
    /// Takes stay-in-building steps out of an NPC's day when the building has no door an NPC may use. Vanilla asks for
    /// the closest usable door without checking the answer: outside the building <c>OnActiveTick</c> throws a
    /// NullReferenceException every tick, and a jump in time enters door -1, an IndexOutOfRangeException.
    /// The buildings only exist in the Main scene, so this runs per NPC instance and not when the pack is read.
    /// </summary>
    internal static class DoorlessStayGuard
    {
        /// <summary>Call before the schedule is enabled. An inactive action never starts and is what S1API treats as an unused slot.</summary>
        public static void Apply(GameObject npc, string npcId)
        {
            int checkedSteps = 0, dropped = 0;
            foreach (NPCEvent_StayInBuilding stay in npc.GetComponentsInChildren<NPCEvent_StayInBuilding>(true))
            {
                if (stay == null || !stay.gameObject.activeSelf) continue;
                checkedSteps++;

                NPCEnterableBuilding building = stay.Building;
                // A step with its own door walks to that door whether it is usable or not, and never asks for the closest one.
                if (building == null || stay.Door != null || HasUsableDoor(building)) continue;

                stay.gameObject.SetActive(false);
                dropped++;
                Core.Log?.Warning($"'{npcId}' schedule action 'stayInBuilding' at {stay.StartTime:D4}: '{building.BuildingName}' has no door an NPC can use - skipped.");
            }
            Core.Log?.Msg($"'{npcId}': {checkedSteps} stay-in-building step(s) checked for a usable door, {dropped} skipped.");
        }

        private static bool HasUsableDoor(NPCEnterableBuilding building)
        {
            var doors = building.Doors;
            if (doors == null) return false;
            for (int i = 0; i < doors.Length; i++)
                if (doors[i] != null && doors[i].Usable) return true;
            return false;
        }
    }
}
