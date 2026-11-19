using System;
using Il2CppScheduleOne.AvatarFramework.Animation;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Map;
using Personnel.Model;
using Personnel.Util;
using UnityEngine;
using UnityEngine.AI;
using GameMap = Il2CppScheduleOne.Map.Map;

namespace Personnel.Spawn
{
    /// <summary>
    /// Where a pack NPC lives: the spawn position, else the place of the first schedule step that names one (in
    /// manifest order). The places mirror what vanilla and S1API walk to for each step:
    /// <list type="bullet">
    /// <item>walkTo, locationDialogue, locationAction, useSlotMachine: the step's position.</item>
    /// <item>stayInBuilding: the access point of the door the NPC uses (doorIndex, else door 0), the point
    /// NPC.ExitBuilding warps to.</item>
    /// <item>sit: the access point of the seat set's first seat.</item>
    /// </list>
    /// ATM, vending machine and car park steps name no fixed place and are passed over. The scene objects only exist
    /// in Main, so this runs per NPC instance.
    /// </summary>
    internal static class HomePoint
    {
        /// <summary>The NPC's home point from its definition, or null when neither spawn nor schedule names a place.</summary>
        public static Vector3? FromDef(NpcDef def, out string source)
        {
            source = null;
            if (def == null) return null;
            if (def.Spawn?.Position is Vector3 spawn) { source = "spawn position"; return spawn; }
            if (def.Schedule == null) return null;

            foreach (NpcScheduleAction a in def.Schedule)
            {
                if (a == null || string.IsNullOrEmpty(a.Type)) continue;
                string type = a.Type.Trim().ToLowerInvariant();
                string at = a.Time >= 0 ? a.Time.ToString("D4") : "(no time)";
                switch (type)
                {
                    case "walkto":
                    case "locationdialogue":
                    case "locationaction":
                    case "useslotmachine":
                        if (a.Position is Vector3 p) { source = $"schedule step '{a.Type}' at {at}"; return p; }
                        break;
                    case "stayinbuilding":
                    {
                        NPCEnterableBuilding b = ScheduleRefGuard.FindBuilding(a.Building);
                        Transform door = DoorAccessPoint(b, a.DoorIndex);
                        if (door != null) { source = $"schedule step 'stayInBuilding' at {at} (door of '{b.BuildingName}')"; return door.position; }
                        break;
                    }
                    case "sit":
                    {
                        AvatarSeatSet set = ScheduleRefGuard.FindSeatSet(a.SeatSetPath, a.SeatSet);
                        Transform seat = SeatAccessPoint(set);
                        if (seat != null) { source = $"schedule step 'sit' at {at} (seat set '{set.gameObject.name}')"; return seat.position; }
                        break;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// A public street point for an NPC with a body and no place at all: the customer stand point of the first
        /// delivery location in its spawn.region (Downtown when unset or unknown). Null when the map is not loaded.
        /// </summary>
        public static Vector3? Fallback(NpcDef def, out string source)
        {
            source = null;
            if (!Singleton<GameMap>.InstanceExists) return null;
            EMapRegion region = EMapRegion.Downtown;
            if (!string.IsNullOrWhiteSpace(def?.Spawn?.Region) && Parse.TryParseEnum(def.Spawn.Region, out S1API.Map.Region r))
                region = (EMapRegion)(int)r;
            var data = Singleton<GameMap>.Instance.GetRegionData(region);
            var locations = data?.RegionDeliveryLocations;
            if (locations == null) return null;
            for (int i = 0; i < locations.Length; i++)
            {
                var stand = locations[i]?.CustomerStandPoint;
                if (stand == null) continue;
                source = $"stand point of delivery location '{locations[i].gameObject.name}' in {region}";
                return stand.position;
            }
            return null;
        }

        /// <summary>The point snapped onto the NavMesh within 2 m, as NPC.ExitBuilding does; the point itself otherwise.</summary>
        public static Vector3 OnNavMesh(Vector3 point)
            => NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : point;

        private static Transform DoorAccessPoint(NPCEnterableBuilding b, int? doorIndex)
        {
            var doors = b?.Doors;
            if (doors == null || doors.Length == 0) return null;
            int i = doorIndex.HasValue && doorIndex.Value >= 0 && doorIndex.Value < doors.Length ? doorIndex.Value : 0;
            return doors[i]?.AccessPoint;
        }

        private static Transform SeatAccessPoint(AvatarSeatSet set)
        {
            var seats = set?.Seats;
            if (seats == null) return null;
            for (int i = 0; i < seats.Length; i++)
            {
                Transform ap = seats[i]?.AccessPoint;
                if (ap != null) return ap;
            }
            return null;
        }
    }
}
