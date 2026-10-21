#if DEBUG
using System;
using System.Text;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe for Personnel customer NPCs: <c>personnel cust &lt;id-filter&gt; [bring|dump|checks|offer|done]</c>.
    /// <c>bring</c> warps the NPC next to the local player, <c>dump</c> (default) logs the customer and behaviour
    /// state the vanilla sale flow depends on, so a stuck sale can be read from the log instead of a screen.
    /// </summary>
    internal static class CustomerProbe
    {
        internal static void Run(string filter, string action)
        {
            if (string.IsNullOrWhiteSpace(filter)) { Log("usage: personnel cust <id-filter> [bring|dump|checks|offer|done]"); return; }
            S1NPC npc = Find(filter, out string id);
            if (npc == null) { Log($"cust: no live NPC matches '{filter}'."); return; }

            switch ((action ?? "dump").ToLowerInvariant())
            {
                case "bring": Bring(npc, id); break;
                case "checks": Checks(npc, id); break;
                case "offer": Offer(npc, id); break;
                case "done": Done(npc, id); break;
                default: Dump(npc, id); break;
            }
        }

        // The reads HandoverScreenCustomerInfoPanel.Open and the "You wanna buy something?" choice depend on.
        private static void Checks(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log($"cust checks {id}: customer MISSING"); return; }
            Log($"cust checks {id}: orderedDrugTypes={Safe(() => c.GetOrderedDrugTypes().Count)} " +
                $"standards={Safe(() => c.CustomerData.Standards)} " +
                $"preferredProps={Safe(() => c.CustomerData.PreferredProperties.Count)} " +
                $"defaultAffinities={Safe(() => c.CustomerData.DefaultAffinityData.ProductAffinities.Count)} " +
                $"currentAffinities={Safe(() => c.currentAffinityData.ProductAffinities.Count)} " +
                $"showOffer={Safe(() => c.ShowOfferDealOption(true))} " +
                $"offerValid={Safe(() => { bool ok = c.OfferDealValid(out string why); return ok + (ok ? "" : "(" + why + ")"); })}");
        }

        // Runs the dialogue choice "You wanna buy something?" (Customer.InstantDealOffered) and reports whether the
        // handover screen opened. The roll inside can refuse; run it again in that case.
        private static void Offer(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log($"cust offer {id}: customer MISSING"); return; }
            try { c.InstantDealOffered(); }
            catch (Exception e) { Log($"cust offer {id}: InstantDealOffered threw {e.GetType().Name}: {e.Message}"); }
            var hs = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>.Instance;
            Log($"cust offer {id}: handoverOpen={Safe(() => hs.IsOpen)} pendingInstantDeal={Safe(() => c.pendingInstantDeal)}");
        }

        // Packages the first product in the player's inventory, puts it into the open handover screen and presses
        // Done - the same Submit path the button takes.
        private static void Done(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            var hs = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>.Instance;
            if (c == null || hs == null || !hs.IsOpen) { Log($"cust done {id}: no open handover screen (run offer first)."); return; }

            var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
            var pack = Il2CppScheduleOne.Registry.GetItem("baggie").TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>();
            Il2CppScheduleOne.ItemFramework.ItemSlot from = null;
            foreach (var slot in inv.GetAllInventorySlots())
                if (slot?.ItemInstance?.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>() != null) { from = slot; break; }
            if (from == null) { Log($"cust done {id}: no product in the inventory (give ogkush 5)."); return; }

            var product = from.ItemInstance.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
            if (product.AppliedPackaging == null && pack != null) product.SetPackaging(pack);
            var item = from.ItemInstance;
            from.ClearStoredInstance();
            hs._customerSlots[0].SetStoredItem(item);

            float cashBefore = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.cashBalance;
            int dealsBefore = c.CompletedDeliveries;
            try { hs.DoneButtonPressed(); }
            catch (Exception e) { Log($"cust done {id}: DoneButtonPressed threw {e.GetType().Name}: {e.Message}"); }
            float cashAfter = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.cashBalance;
            Log($"cust done {id}: handed '{Safe(() => item.Name)}' x{Safe(() => item.Quantity)}; handoverOpen={Safe(() => hs.IsOpen)} " +
                $"cash {cashBefore} -> {cashAfter}, completedDeliveries {dealsBefore} -> {Safe(() => c.CompletedDeliveries)}, " +
                $"timeSinceLastDeal={Safe(() => c.TimeSinceLastDealCompleted)}");
        }

        internal static S1NPC Find(string filter, out string id)
        {
            id = null;
            var reg = Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry;
            if (reg == null) return null;
            foreach (var n in reg)
            {
                if (n == null || n.ID == null) continue;
                if (n.ID.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                id = n.ID;
                return n;
            }
            return null;
        }

        private static void Bring(S1NPC npc, string id)
        {
            Player local = Player.Local;
            if (local == null) { Log("cust bring: no local player."); return; }
            Vector3 target = local.transform.position + local.transform.forward * 2f;
            npc.Movement.Warp(target);
            Log($"cust bring: warped {id} to {target}.");
        }

        private static void Dump(S1NPC npc, string id)
        {
            var sb = new StringBuilder();
            sb.Append($"cust dump {id}: ");
            sb.Append($"relUnlocked={Safe(() => npc.RelationData.Unlocked)} ");
            sb.Append($"relDelta={Safe(() => npc.RelationData.RelationDelta)} ");
            sb.Append($"unlockType={Safe(() => npc.RelationData.UnlockType)} ");

            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log(sb.Append("customer=MISSING").ToString()); return; }
            sb.Append($"customer.NPC={(Safe(() => c.NPC != null))} ");
            sb.Append($"data={(Safe(() => c.CustomerData != null))} ");
            sb.Append($"inUnlocked={Safe(() => S1Customer.UnlockedCustomers.Contains(c))} ");
            sb.Append($"inLocked={Safe(() => S1Customer.LockedCustomers.Contains(c))} ");
            sb.Append($"awaitingDelivery={Safe(() => c.IsAwaitingDelivery)} ");
            sb.Append($"contract={Safe(() => c.CurrentContract != null)} ");
            sb.Append($"recommended={Safe(() => c.HasBeenRecommended)} ");
            sb.Append($"component.enabled={Safe(() => c.enabled)} ");
            sb.Append($"attendDeal={Safe(() => c._attendDealBehaviour != null)} ");
            sb.Append($"offerDealChoice={Safe(() => c.offerDealChoice != null)} ");
            sb.Append($"sampleChoice={Safe(() => c.sampleChoice != null)} ");
            Log(sb.ToString());

            var beh = npc.Behaviour;
            if (beh == null) { Log("  behaviour manager MISSING"); return; }
            Log($"  activeBehaviour={Safe(() => beh.activeBehaviour != null ? beh.activeBehaviour.Name : "none")} stack={Safe(() => beh.behaviourStack.Count)}");
            var all = npc.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Behaviour.Behaviour>(true);
            foreach (var b in all)
                Log($"  behaviour '{Safe(() => b.Name)}' ({b.GetIl2CppType().Name}) enabled={Safe(() => b.Enabled)} active={Safe(() => b.Active)} prio={Safe(() => b.Priority)} index={Safe(() => b.BehaviourIndex)} inStack={Safe(() => beh.behaviourStack.Contains(b))}");

            var dh = npc.DialogueHandler;
            Log($"  dialogueHandler={(dh != null)} ");
        }

        internal static string Safe<T>(Func<T> f)
        {
            try { return Convert.ToString(f()); }
            catch (Exception e) { return "ERR(" + e.GetType().Name + ")"; }
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
