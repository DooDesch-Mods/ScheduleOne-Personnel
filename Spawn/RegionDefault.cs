using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.NPCs.Schedules;
using Personnel.Model;
using UnityEngine;
using GameMap = Il2CppScheduleOne.Map.Map;

namespace Personnel.Spawn
{
    /// <summary>
    /// Region of an NPC whose pack sets no spawn.region. Vanilla's NPC.Region field defaults to Downtown, and the free
    /// sample asks for the NPC's region to be unlocked (Customer.SampleOptionValid), so a customer who lives in
    /// Northtown would show "'Downtown' region must be unlocked" until the player reaches Downtown. The region is taken
    /// from where the NPC lives instead: the spawn position, else the first schedule step with a position, else the
    /// first stay-in-building step's building. The buildings only exist in the Main scene, so this runs per NPC
    /// instance.
    /// </summary>
    internal static class RegionDefault
    {
        /// <summary>The region at the NPC's home point, or null when the def gives no point or the map is missing.</summary>
        public static S1API.Map.Region? Resolve(GameObject npc, NpcDef def)
        {
            string source;
            Vector3? point = HomePoint(npc, def, out source);
            if (point == null)
            {
                Core.Log?.Msg($"'{def.Id}': no spawn.region and no position in spawn or schedule - the region stays at the game's default.");
                return null;
            }
            if (!Singleton<GameMap>.InstanceExists)
            {
                Core.Log?.Warning($"'{def.Id}': no spawn.region and the map is not loaded yet - the region stays at the game's default.");
                return null;
            }
            var region = (S1API.Map.Region)(int)Singleton<GameMap>.Instance.GetRegionFromPosition(point.Value);
            Core.Log?.Msg($"'{def.Id}': no spawn.region - region {region} taken from the {source} at {point.Value}.");
            return region;
        }

        private static Vector3? HomePoint(GameObject npc, NpcDef def, out string source)
        {
            if (def.Spawn?.Position is Vector3 spawn) { source = "spawn position"; return spawn; }
            if (def.Schedule != null)
                foreach (var step in def.Schedule)
                    if (step?.Position is Vector3 p) { source = $"schedule step '{step.Type}'"; return p; }
            foreach (NPCEvent_StayInBuilding stay in npc.GetComponentsInChildren<NPCEvent_StayInBuilding>(true))
            {
                if (stay == null || !stay.gameObject.activeSelf || stay.Building == null) continue;
                source = $"building '{stay.Building.BuildingName}'";
                return stay.Building.transform.position;
            }
            source = null;
            return null;
        }
    }
}
