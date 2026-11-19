using Personnel.Model;
using UnityEngine;

namespace Personnel.Spawn
{
    /// <summary>
    /// Places a pack NPC with a body that has no spawn.position. S1API only moves an NPC whose prefab registered a spawn
    /// position (NPC.FinalizeNetworkSpawn), so without one the NPC stays at the world origin, the alley behind the
    /// Central gas mart, until its schedule walks it away; a step it joins late (set time, load mid-day) can leave it
    /// there for hours (#104). The NPC starts at its home point instead (<see cref="HomePoint.FromDef"/>); one without
    /// any place gets a public street point of its region and a warning naming pack and NPC.
    /// Runs from OnCreated, before Schedule.Enable, so the first schedule step already starts from that point.
    /// NPCMovement.Warp is host-only and sends the position to the clients itself.
    /// </summary>
    internal static class SpawnPlacement
    {
        public static void Apply(S1API.Entities.NPC npc, NpcDef def)
        {
            if (npc == null || def == null) return;
            if (def.Spawn?.Position != null) return;
            string pack = string.IsNullOrWhiteSpace(def.Source) ? "(unknown pack)" : def.Source;

            Vector3? point = HomePoint.FromDef(def, out string source);
            if (point == null)
            {
                point = HomePoint.Fallback(def, out source);
                if (point == null)
                {
                    Core.Log?.Error($"[spawn] pack '{pack}' NPC '{def.Id}': no spawn.position, no schedule step with a place and no fallback point (map not loaded) - it stays at {npc.Position}.");
                    return;
                }
                Core.Log?.Warning($"[spawn] pack '{pack}' NPC '{def.Id}': no spawn.position and no schedule step with a place - placed at the {source}. Give it a spawn.position.");
            }

            if (!Il2CppFishNet.InstanceFinder.IsServer)
            {
                Core.Log?.Msg($"[spawn] '{def.Id}': start point is the {source} - the host places it.");
                return;
            }
            Vector3 at = HomePoint.OnNavMesh(point.Value);
            npc.Position = at;
            Core.Log?.Msg($"[spawn] '{def.Id}': no spawn.position - placed at the {source}, {at}.");
        }
    }
}
