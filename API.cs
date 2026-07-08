using System;
using System.Collections.Generic;
using DooDesch.AvatarKit;
using Il2CppScheduleOne.AvatarFramework;
using Personnel.Appearance;
using Personnel.Model;
using Personnel.Registration;
using S1API.Entities;
using UnityEngine;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;
using S1NPCs = Il2CppScheduleOne.NPCs;

namespace Personnel
{
    /// <summary>
    /// Public, stable entry point for other mods (e.g. Backrooms) that want to consume user-authored NPC
    /// definitions. Reference Personnel.dll and declare <c>[MelonOptionalDependencies("Personnel")]</c>. The
    /// primitive most consumers need is <see cref="BuildAvatarSettings"/> / <see cref="ApplyAppearance"/> - realise
    /// a definition's look onto any live <c>Avatar</c>. Definitions are loaded from packs on startup; register your
    /// own programmatically via <see cref="Register"/>.
    /// </summary>
    public static class API
    {
        /// <summary>All loaded NPC definitions (pack-sourced + API-registered).</summary>
        public static IReadOnlyList<NpcDef> All => NpcRegistry.AllDefs;

        /// <summary>Look up a definition by id (case-insensitive).</summary>
        public static bool TryGet(string id, out NpcDef def) => NpcRegistry.TryGet(id, out def);

        /// <summary>Fired after the packs folder is rescanned (<see cref="Reload"/>). Re-register/refresh here.</summary>
        public static event Action OnReloaded
        {
            add => NpcRegistry.OnReloaded += value;
            remove => NpcRegistry.OnReloaded -= value;
        }

        /// <summary>Register a definition programmatically (de-duplicated by Source/Id). Returns false on duplicate.</summary>
        public static bool Register(NpcDef def) => NpcRegistry.Add(def);

        /// <summary>
        /// Realise a definition's appearance into a vanilla <see cref="AvatarSettings"/>. Registers any custom PNG
        /// layers on first use. Call from the main thread (it touches Unity resources).
        /// </summary>
        public static AvatarSettings BuildAvatarSettings(NpcDef def) => AvatarSettingsFactory.BuildAvatarSettings(def);

        /// <summary>Build the definition's <see cref="AvatarSettings"/> and load it onto a live avatar.</summary>
        public static bool ApplyAppearance(Avatar avatar, NpcDef def)
        {
            if (avatar == null || def == null) return false;
            AvatarSettings s = BuildAvatarSettings(def);
            if (s == null) return false;
            avatar.LoadAvatarSettings(s);
            return true;
        }

        /// <summary>
        /// Apply a definition's extreme body distortion (Personify's Experimental tab: bone scale/hide, mesh hide)
        /// onto a live avatar. A separate pass from <see cref="ApplyAppearance"/> since vanilla AvatarSettings has
        /// no such concept - call this right after loading the normal appearance.
        /// </summary>
        public static void ApplyDistortion(Avatar avatar, NpcDef def)
        {
            if (avatar == null || def?.Appearance == null) return;
            var entries = new Dictionary<string, (Vector3 scale, bool hide)>();
            foreach (var kv in def.Appearance.Distortion)
                if (kv.Value != null) entries[kv.Key] = (kv.Value.Scale, kv.Value.Hide);
            AvatarDistortion.Apply(avatar, entries);
        }

        /// <summary>Rescan the packs folder (drops+reloads pack-sourced defs, keeps API-registered ones).</summary>
        public static void Reload() => NpcRegistry.Reload();

        /// <summary>
        /// Configures an S1API <see cref="NPCPrefabBuilder"/> from a definition - identity + full appearance (+ the
        /// def's spawn position if it has one). This is the one call needed to turn a Personnel definition into a real,
        /// networked, save/load-safe S1API NPC. Prefer subclassing <see cref="PersonnelNpc"/> (which calls this for
        /// you); call this directly only if you keep your own <c>NPC</c> subclass and want
        /// to configure it inside its <c>ConfigurePrefab</c>.
        /// </summary>
        public static void ConfigureFromDef(NPCPrefabBuilder builder, NpcDef def)
        {
            if (builder == null || def == null) return;
            SplitName(def.DisplayName, out string first, out string last);
            builder.WithIdentity(def.Id, first, last);
            builder.WithAppearanceDefaults(ab => AvatarSettingsFactory.ApplyToDefaults(ab, def));

            // Make the NPC a real phone contact. Vanilla ContactsDetailPanel shows "???" and hides the
            // map button for any NPC whose relationship is locked; unlocking it once (respected against
            // save data by S1API) makes the real name render. This is a cosmetic contact only - no
            // economy role - which also keeps clear of the vanilla NRE that triggers when a role-less NPC
            // is left in the "mutually known but locked" state.
            builder.WithRelationshipDefaults(r => r.SetUnlocked(true));

            // Optional economy role, opt-in per pack via behavior.conversation. Default ("none"/null)
            // stays a plain cosmetic contact - not every NPC should be a customer.
            string role = def.Behavior?.Conversation;
            if (string.Equals(role, "customer", StringComparison.OrdinalIgnoreCase))
                builder.WithCustomerDefaults(_ => { });
            else if (string.Equals(role, "dealer", StringComparison.OrdinalIgnoreCase))
                builder.WithDealerDefaults(_ => { });

            if (def.Spawn?.Position != null)
                builder.WithSpawnPosition(def.Spawn.Position.Value, Quaternion.identity);
        }

        /// <summary>
        /// Adds a map marker (the game's own <c>NPCPoI</c>) to a live custom NPC so players can find it on the
        /// phone map. Client-local and cosmetic - no economy role, no networking. Safe to call once the NPC
        /// object exists (e.g. from <see cref="PersonnelNpc"/>'s creation hook); no-ops if the map manager or
        /// NPC component is not ready.
        /// </summary>
        public static void AddMapMarker(GameObject npcRoot)
        {
            if (npcRoot == null || !S1NPCs.NPCManager.InstanceExists) return;
            var mgr = S1NPCs.NPCManager.Instance;
            if (mgr == null || mgr.NPCPoIPrefab == null) return;
            var npc = npcRoot.GetComponent<S1NPCs.NPC>();
            if (npc == null) return;

            var poi = UnityEngine.Object.Instantiate(mgr.NPCPoIPrefab, npcRoot.transform);
            poi.SetNPC(npc);
            poi.enabled = true;
        }

        // "Pale Faceling" -> ("Pale", "Faceling"); single word -> (word, ""); empty -> ("NPC", "").
        private static void SplitName(string display, out string first, out string last)
        {
            first = "NPC"; last = string.Empty;
            if (string.IsNullOrWhiteSpace(display)) return;
            display = display.Trim();
            int sp = display.IndexOf(' ');
            if (sp > 0) { first = display.Substring(0, sp); last = display.Substring(sp + 1).Trim(); }
            else first = display;
        }
    }
}
