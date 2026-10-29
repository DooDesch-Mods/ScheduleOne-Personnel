#if DEBUG
using System;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using S1DeadDrop = Il2CppScheduleOne.Economy.DeadDrop;
using S1Supplier = Il2CppScheduleOne.Economy.Supplier;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe for supplier NPCs: <c>personnel supplier &lt;id&gt; [dump|order &lt;item&gt; &lt;qty&gt;|ready]</c>.
    /// <c>dump</c> logs status, debt, limit, the dead-drop assortment and the contact picture. <c>order</c> places a
    /// dead-drop order through the same server RPCs the phone uses (<c>SetDeaddrop</c>, <c>ChangeDebt</c>);
    /// <c>ready</c> completes it now and logs which dead drop received what.
    /// </summary>
    internal static class SupplierProbe
    {
        internal static void Run(string filter, string action, string a1, string a2)
        {
            if (string.IsNullOrWhiteSpace(filter)) { Log("usage: personnel supplier <id> [dump|order <item> <qty>|ready]"); return; }
            var npc = CustomerProbe.Find(filter, out string id);
            if (npc == null) { Log($"supplier: no live NPC matches '{filter}'."); return; }
            S1Supplier s = npc.TryCast<S1Supplier>() ?? npc.GetComponent<S1Supplier>();
            if (s == null) { Log($"supplier {id}: no Supplier component."); return; }

            switch ((action ?? "dump").ToLowerInvariant())
            {
                case "order": Order(s, id, a1, a2); break;
                case "ready": Ready(s, id); break;
                default: Dump(s, id); break;
            }
        }

        private static void Dump(S1Supplier s, string id)
        {
            var sb = new StringBuilder($"supplier {id}: status={CustomerProbe.Safe(() => s.Status)} unlocked={CustomerProbe.Safe(() => s.RelationData.Unlocked)} " +
                                       $"debt={CustomerProbe.Safe(() => s.Debt)} limit={CustomerProbe.Safe(() => s.GetDeadDropLimit())} " +
                                       $"minsUntilReady={CustomerProbe.Safe(() => s.MinsUntilDeaddropReady)} items=[");
            try
            {
                var listings = s.SupplierData?.DeliveryShopListings;
                for (int i = 0; listings != null && i < listings.Length; i++)
                {
                    var it = listings[i]?.Item;
                    sb.Append(i > 0 ? ", " : "").Append(it == null ? "<null>" : it.ID + "@" + it.BasePurchasePrice);
                }
            }
            catch (Exception e) { sb.Append("ERR(" + e.GetType().Name + ")"); }
            sb.Append("] mugshot=").Append(CustomerProbe.Safe(() =>
            {
                var m = s.MugshotSprite;
                return m == null ? "none" : m.name + " " + m.texture.width + "x" + m.texture.height;
            }));
            Log(sb.ToString());
        }

        private static void Order(S1Supplier s, string id, string item, string qtyArg)
        {
            if (string.IsNullOrWhiteSpace(item)) { Log("usage: personnel supplier <id> order <item> <qty>"); return; }
            int qty = int.TryParse(qtyArg, out int q) && q > 0 ? q : 1;
            float price = 0f;
            try { price = Il2CppScheduleOne.Registry.GetItem(item).TryCast<Il2CppScheduleOne.ItemFramework.StorableItemDefinition>().BasePurchasePrice * qty; }
            catch (Exception e) { Log($"supplier {id}: price lookup for '{item}' failed ({e.GetType().Name}) - debt stays."); }
            try
            {
                var arr = new Il2CppReferenceArray<StringIntPair>(1);
                arr[0] = new StringIntPair(item, qty);
                s.SetDeaddrop(arr, 30);
                if (price > 0f) s.ChangeDebt(price);
                Log($"supplier {id}: ordered {qty}x {item} for {price}; debt={CustomerProbe.Safe(() => s.Debt)} minsUntilReady={CustomerProbe.Safe(() => s.MinsUntilDeaddropReady)}");
            }
            catch (Exception e) { Log($"supplier {id}: order failed {e.GetType().Name}: {e.Message}"); }
        }

        private static void Ready(S1Supplier s, string id)
        {
            try { s.CompleteDeaddrop(); }
            catch (Exception e) { Log($"supplier {id}: CompleteDeaddrop threw {e.GetType().Name}: {e.Message}"); return; }
            int n = 0;
            foreach (S1DeadDrop d in S1DeadDrop.DeadDrops)
            {
                if (d == null || d.Storage == null || d.Storage.ItemCount == 0) continue;
                var sb = new StringBuilder($"supplier {id}: dead drop '{d.DeadDropName}' holds ");
                foreach (var slot in d.Storage.ItemSlots)
                    if (slot?.ItemInstance != null) sb.Append(slot.ItemInstance.ID).Append(" x").Append(slot.Quantity).Append(' ');
                Log(sb.ToString());
                n++;
            }
            if (n == 0) Log($"supplier {id}: no dead drop holds anything after CompleteDeaddrop.");
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
