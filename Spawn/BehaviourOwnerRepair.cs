using System;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs.Behaviour;
using UnityEngine;
using Behaviour = Il2CppScheduleOne.NPCs.Behaviour.Behaviour;

namespace Personnel.Spawn
{
    /// <summary>
    /// Points every behaviour of an NPC at the NPC's own <see cref="NPCBehaviour"/>. S1API adds the
    /// "Customer attend deal" behaviour to the prefab at runtime and sets its owner only on the host, so on a co-op
    /// client <c>Behaviour.beh</c> stays null. The host's Enable/Activate RPCs then throw inside <c>Behaviour.Npc</c>,
    /// FishNet drops the exception, and the customer never waits at the drop: scheduled deals offer no handover.
    /// The same missing owner leaves <c>CustomerAttendDealBehaviour._customer</c> unset, so that is repaired too.
    /// </summary>
    internal static class BehaviourOwnerRepair
    {
        public static void Apply(GameObject npc, string npcId)
        {
            NPCBehaviour manager = npc.GetComponentInChildren<NPCBehaviour>(true);
            if (manager == null)
            {
                Core.Log?.Warning($"'{npcId}': no NPCBehaviour found - behaviour owners not checked.");
                return;
            }

            int checkedCount = 0, repaired = 0;
            foreach (Behaviour behaviour in manager.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null) continue;
                checkedCount++;
                NPCBehaviour owner = behaviour.beh;
                if (owner == null || owner.Pointer != manager.Pointer)
                {
                    behaviour.beh = manager;
                    repaired++;
                    Core.Log?.Msg($"'{npcId}': behaviour '{behaviour.Name}' had {(owner == null ? "no owner" : "a foreign owner")} - set to this NPC.");
                }

                // The attend behaviour caches its Customer in Awake through Npc, which needs beh. Where Awake ran
                // without an owner, _customer stays null and Activate throws before it marks the customer as waiting.
                CustomerAttendDealBehaviour attend = behaviour.TryCast<CustomerAttendDealBehaviour>();
                if (attend != null && attend._customer == null)
                {
                    Customer customer = npc.GetComponent<Customer>();
                    if (customer == null)
                    {
                        Core.Log?.Warning($"'{npcId}': '{behaviour.Name}' has no Customer and the NPC has none either - left as is.");
                        continue;
                    }
                    attend._customer = customer;
                    repaired++;
                    Core.Log?.Msg($"'{npcId}': '{behaviour.Name}' had no Customer - set to this NPC's.");
                }
            }
            Core.Log?.Msg($"'{npcId}': {checkedCount} behaviour owner(s) checked, {repaired} repaired.");
        }
    }
}
