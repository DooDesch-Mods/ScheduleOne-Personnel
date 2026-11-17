#if DEBUG
using System.Text;
using Il2CppFishNet.Object;
using UnityEngine;
using Object = UnityEngine.Object;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;
using Handler = Il2CppScheduleOne.Equipping.EquippedItemHandler;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe for networked equipped items: <c>personnel equip [objectId]</c>.
    /// Without an argument it lists every <c>EquippedItemHandler</c> with its synced user, the user's NetworkObject id and
    /// the ComponentIndex FishNet resolves the user by. With an object id it lists that NetworkObject's NetworkBehaviours,
    /// so host and client can be compared index by index.
    /// </summary>
    internal static class EquipProbe
    {
        internal static void Run(string arg, string filter)
        {
            if (arg == "smoke") { Smoke(filter); return; }
            if (int.TryParse(arg, out int id)) { Nob(id); return; }

            var handlers = Object.FindObjectsOfType<Handler>(true);
            int missing = 0;
            foreach (Handler handler in handlers)
            {
                var raw = handler._user;
                NetworkBehaviour user = raw == null ? null : ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)(object)raw).TryCast<NetworkBehaviour>();
                if (raw == null) missing++;
                S1NPC npc = user != null ? user.GetComponentInParent<S1NPC>() : null;
                Log($"equip: handler object {CustomerProbe.Safe(() => handler.ObjectId)} data {CustomerProbe.Safe(() => handler._equippableData != null ? handler._equippableData.name : "null")} " +
                    $"equipped {handler.IsEquipped} user {(raw == null ? "null" : user == null ? "not a NetworkBehaviour" : user.GetIl2CppType().Name)} " +
                    $"npc {CustomerProbe.Safe(() => npc != null ? npc.ID : "-")} userObject {CustomerProbe.Safe(() => user != null ? user.ObjectId.ToString() : "-")} " +
                    $"userIndex {CustomerProbe.Safe(() => user != null ? user.ComponentIndex.ToString() : "-")} server {Il2CppFishNet.InstanceFinder.IsServer}");
            }
            Log($"equip: {handlers.Length} handler(s), {missing} without a user.");
        }

        private static void Nob(int objectId)
        {
            foreach (NetworkObject nob in Object.FindObjectsOfType<NetworkObject>(true))
            {
                if (nob == null || nob.ObjectId != objectId || !nob.IsSpawned) continue;
                var list = nob.NetworkBehaviours;
                int count = list?.Length ?? -1;
                var sb = new StringBuilder($"equip nob {objectId}: '{nob.gameObject.name}' prefab {CustomerProbe.Safe(() => nob.PrefabId)} " +
                    $"collection {CustomerProbe.Safe(() => nob.SpawnableCollectionId)} scene {CustomerProbe.Safe(() => nob.SceneId)} behaviours {count}:");
                for (int i = 0; i < count; i++)
                {
                    var b = list[i];
                    sb.Append(b == null ? $" [{i}]null" : $" [{i}]{CustomerProbe.Safe(() => b.ComponentIndex)}:{b.GetIl2CppType().Name}");
                }
                Log(sb.ToString());
                return;
            }
            Log($"equip nob {objectId}: no spawned NetworkObject with this id on this peer.");
        }

        // Host only: puts the vanilla cigarette into the NPC's hand as a networked item, the way SmokeCigarette.Begin does,
        // so a check does not wait for a smoke break.
        private static void Smoke(string filter)
        {
            if (!Il2CppFishNet.InstanceFinder.IsServer) { Log("equip smoke: host only."); return; }
            S1NPC npc = CustomerProbe.Find(filter, out string id);
            if (npc == null) { Log($"equip smoke: no live NPC matches '{filter}'."); return; }
            var data = Resources.Load<Il2CppScheduleOne.Core.Equipping.Framework.EquippableData>("equippables/cigarette/Cigarette");
            if (data == null) { Log("equip smoke: cigarette data not found at equippables/cigarette/Cigarette."); return; }
            var handler = npc.Equip_Networked(data);
            Log($"equip smoke {id}: handler {(handler == null ? "null" : "object " + CustomerProbe.Safe(() => handler.TryCast<Handler>().ObjectId))}, " +
                $"npc object {CustomerProbe.Safe(() => npc.ObjectId)} index {CustomerProbe.Safe(() => npc.ComponentIndex)}.");
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
