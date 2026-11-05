using System;
using System.Collections.Generic;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1Dealer = Il2CppScheduleOne.Economy.Dealer;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;
using S1Police = Il2CppScheduleOne.Police.PoliceOfficer;
using S1Supplier = Il2CppScheduleOne.Economy.Supplier;
using Personnel.Model;

namespace Personnel.Replacements
{
    /// <summary>
    /// Which vanilla NPCs a pack entry may take over, and whether its role fits. Allowed are customers, dealers,
    /// suppliers, police officers and townsfolk; story and quest NPCs (shop keepers, quest givers, cartel, employees)
    /// keep their own look because the story talks about them by face and name.
    /// </summary>
    internal static class ReplacementRules
    {
        // Ids from the game's NPC data assets (0.4.7f12). Quests and shops reference these NPCs directly.
        private static readonly Dictionary<string, string> BlockedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["uncle_nelson"] = "a story NPC", ["thomas_benzies"] = "a story NPC", ["ray_hoffman"] = "a story NPC",
            ["stan_carney"] = "a shop NPC", ["oscar_holland"] = "a shop NPC", ["jeremy_wilkinson"] = "a shop NPC",
            ["marco_baron"] = "a shop NPC", ["ming"] = "a story NPC", ["dan_samwell"] = "a shop NPC",
            ["fiona_hancock"] = "a shop NPC", ["herbert_bleuball"] = "a shop NPC", ["igor_romanovich"] = "a story NPC",
            ["mick_lubbin"] = "a shop NPC", ["sam_thompson"] = "a story NPC", ["anna_chesterfield"] = "a shop NPC",
            ["dean_webster"] = "a shop NPC", ["billy_kramer"] = "a story NPC", ["manny_oakfield"] = "a story NPC",
            ["carteldealer"] = "part of the cartel", ["cartelgoon"] = "part of the cartel", ["sewerking"] = "a story NPC",
            ["sewergoblin"] = "a story NPC", ["weapon_thief"] = "a story NPC",
            // Two officers share this placeholder id, so it does not name one NPC.
            ["officername"] = "not a unique NPC",
        };

        // Vanilla NPC classes with story code of their own; the scene-placed ones (Fixer, Steve) have no fixed id.
        private static readonly Dictionary<string, string> BlockedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["UncleNelson"] = "a story NPC", ["Thomas"] = "a story NPC", ["Ray"] = "a story NPC", ["Stan"] = "a shop NPC",
            ["Oscar"] = "a shop NPC", ["Jeremy"] = "a shop NPC", ["Marco"] = "a shop NPC", ["Fixer"] = "a story NPC",
            ["Ming"] = "a story NPC", ["Steve"] = "a shop NPC", ["Igor"] = "a story NPC", ["Mick"] = "a shop NPC",
            ["Sam"] = "a story NPC", ["Billy"] = "a story NPC", ["Donna"] = "a story NPC", ["Meg"] = "a story NPC",
            ["Jerry"] = "a story NPC", ["Chloe"] = "a story NPC", ["Doris"] = "a story NPC",
            ["CartelGoon"] = "part of the cartel", ["CartelDealer"] = "part of the cartel",
            ["Employee"] = "an employee", ["Botanist"] = "an employee", ["Chemist"] = "an employee",
            ["Cleaner"] = "an employee", ["Packager"] = "an employee",
            ["SpecialCustomer"] = "a special customer", ["SpecialCustomerLeader"] = "a special customer",
        };

        /// <summary>Why <paramref name="vanillaId"/> cannot be replaced, or null when it can (id check only).</summary>
        public static string BlockReason(string vanillaId)
        {
            if (string.IsNullOrWhiteSpace(vanillaId)) return "no NPC";
            return BlockedIds.TryGetValue(vanillaId.Trim(), out string why) ? why : null;
        }

        /// <summary>Why the live NPC cannot be replaced (id and class), or null when it can.</summary>
        public static string BlockReason(S1NPC npc)
        {
            if (npc == null) return "no NPC";
            if (BlockReason(npc.ID) is string byId) return byId;
            try
            {
                for (var t = npc.GetIl2CppType(); t != null; t = t.BaseType)
                {
                    if (t.Name == "NPC") break;
                    if (BlockedTypes.TryGetValue(t.Name, out string why)) return why;
                }
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"[replace] class check of '{npc.ID}' failed ({e.GetType().Name}: {e.Message}) - treated as blocked.");
                return "not checkable";
            }
            return null;
        }

        /// <summary>Role of a live vanilla NPC: "dealer", "supplier", "police", "customer" or "civilian".</summary>
        public static string RoleOf(S1NPC npc)
        {
            if (npc == null) return "civilian";
            if (npc.TryCast<S1Dealer>() != null) return "dealer";
            if (npc.TryCast<S1Supplier>() != null) return "supplier";
            if (npc.TryCast<S1Police>() != null) return "police";
            if (npc.GetComponent<S1Customer>() != null) return "customer";
            return "civilian";
        }

        /// <summary>Role of a definition, resolved the way Personnel spawns it (dealer, supplier, police, customer, behavior.conversation).</summary>
        public static string RoleOf(NpcDef def)
        {
            if (def == null) return "civilian";
            if (def.Dealer != null) return "dealer";
            if (def.Supplier != null) return "supplier";
            if (def.Police != null) return "police";
            if (def.Customer != null) return "customer";
            string conv = def.Behavior?.Conversation;
            if (string.Equals(conv, "customer", StringComparison.OrdinalIgnoreCase)) return "customer";
            if (string.Equals(conv, "dealer", StringComparison.OrdinalIgnoreCase)) return "dealer";
            return "civilian";
        }

        /// <summary>
        /// Whether a definition of role <paramref name="defRole"/> may take over an NPC of role <paramref name="npcRole"/>.
        /// A definition without a role block (look only) fits a customer and a townsperson alike: it changes no role value.
        /// </summary>
        public static bool RoleFits(string defRole, string npcRole)
        {
            if (defRole == npcRole) return true;
            return defRole == "civilian" && npcRole == "customer";
        }
    }
}
