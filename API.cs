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
    /// <remarks>
    /// A definition id is lower case: letters and digits, every other run of characters turned into "_". A pack entry
    /// uses its authored <c>id</c>, or else <c>&lt;packId or folder name&gt;_&lt;npc name&gt;</c>, e.g. "examples_pale".
    /// Packs load from UserData/Personnel/Packs/&lt;pack&gt;/manifest.json on startup.
    /// </remarks>
    /// <example>
    /// Give a live avatar the look of a pack NPC:
    /// <code>
    /// if (Personnel.API.TryGet("examples_pale", out var def))
    /// {
    ///     Personnel.API.ApplyAppearance(avatar, def);
    ///     Personnel.API.ApplyDistortion(avatar, def);
    /// }
    /// </code>
    /// </example>
    public static class API
    {
        /// <summary>All loaded NPC definitions (pack-sourced + API-registered).</summary>
        /// <value>Pack-sourced and API-registered definitions together. Empty when no pack is installed and nothing was registered.</value>
        public static IReadOnlyList<NpcDef> All => NpcRegistry.AllDefs;

        /// <summary>Look up a definition by id (case-insensitive).</summary>
        /// <param name="id">A definition id such as "examples_pale", the same value as <see cref="NpcDef.Id"/>. Null or blank returns false.</param>
        /// <param name="def">The first definition with that id, or null when the method returns false.</param>
        public static bool TryGet(string id, out NpcDef def) => NpcRegistry.TryGet(id, out def);

        /// <summary>Fired after the packs folder is rescanned (<see cref="Reload"/>). Re-register/refresh here.</summary>
        public static event Action OnReloaded
        {
            add => NpcRegistry.OnReloaded += value;
            remove => NpcRegistry.OnReloaded -= value;
        }

        /// <summary>Register a definition programmatically (de-duplicated by Source/Id). Returns false on duplicate.</summary>
        /// <param name="def">
        /// The definition. <see cref="NpcDef.Id"/> must be set, or the call returns false. An empty
        /// <see cref="NpcDef.Source"/> becomes "API". A second definition with the same Source and Id, ignoring case,
        /// returns false. Leave <see cref="NpcDef.PackDir"/> null: a definition with a PackDir counts as pack-sourced and
        /// <see cref="Reload"/> drops it.
        /// </param>
        public static bool Register(NpcDef def) => NpcRegistry.Add(def);

        /// <summary>
        /// Realise a definition's appearance into a vanilla <see cref="AvatarSettings"/>. Registers any custom PNG
        /// layers on first use. Call from the main thread (it touches Unity resources).
        /// </summary>
        /// <param name="def">The definition, e.g. from <see cref="TryGet"/>.</param>
        /// <returns>The settings, or null when <paramref name="def"/> is null.</returns>
        public static AvatarSettings BuildAvatarSettings(NpcDef def) => AvatarSettingsFactory.BuildAvatarSettings(def);

        /// <summary>Build the definition's <see cref="AvatarSettings"/> and load it onto a live avatar.</summary>
        /// <param name="avatar">A live avatar in the scene.</param>
        /// <param name="def">The definition, e.g. from <see cref="TryGet"/>.</param>
        /// <returns>false when the avatar or the definition is null or no settings could be built.</returns>
        public static bool ApplyAppearance(Avatar avatar, NpcDef def)
        {
            if (avatar == null || def == null) return false;
            AvatarSettings s = BuildAvatarSettings(def);
            if (s == null) return false;
            AvatarLayerSlots.LoadAndClean(avatar, s);
            return true;
        }

        /// <summary>
        /// Apply a definition's extreme body distortion (Personify's Experimental tab: bone scale/hide, mesh hide)
        /// onto a live avatar. A separate pass from <see cref="ApplyAppearance"/> since vanilla AvatarSettings has
        /// no such concept - call this right after loading the normal appearance.
        /// </summary>
        /// <param name="avatar">A live avatar, with its normal appearance already loaded.</param>
        /// <param name="def">
        /// The definition. Its <c>Appearance.Distortion</c> keys are the bones "HeadBone", "HipBone", "LeftFootBone",
        /// "RightFootBone", "LeftShoulder", "RightShoulder", "MiddleSpine", "LowerSpine" and "LowestSpine", which take
        /// a scale or a hide, plus "FaceMesh" and "BodyMesh0", "BodyMesh1" and so on, which only take a hide. A bone
        /// missing from the dictionary goes back to scale 1 and visible. Other keys are ignored.
        /// </param>
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
        /// <param name="builder">The builder S1API passes to your NPC's <c>ConfigurePrefab</c>.</param>
        /// <param name="def">
        /// The definition. Its <see cref="NpcDef.SaveId"/>, or else its Id, becomes the S1API identity that saves store.
        /// The display name splits at the first space into first and last name; an empty name becomes "NPC".
        /// </param>
        /// <example>
        /// <code>
        /// protected override void ConfigurePrefab(NPCPrefabBuilder builder)
        /// {
        ///     if (Personnel.API.TryGet("examples_pale", out var def))
        ///         Personnel.API.ConfigureFromDef(builder, def);
        /// }
        /// </code>
        /// </example>
        public static void ConfigureFromDef(NPCPrefabBuilder builder, NpcDef def)
        {
            if (builder == null || def == null) return;
            SplitName(def.DisplayName, out string first, out string last);
            builder.WithIdentity(def.SaveId ?? def.Id, first, last);
            builder.WithAppearanceDefaults(ab =>
            {
                AvatarSettingsFactory.ApplyToDefaults(ab, def);
                ApplyImpostorIfSupported(ab, def);
            });

            string role = ResolveRole(def);

            // Phone-contact presentation. Unlocking the relationship makes ContactsDetailPanel render the
            // real name instead of "???" (respected against save data by S1API); it also keeps clear of the
            // vanilla NRE that triggers when a role-less NPC is left "mutually known but locked".
            // contact.visible=false skips the unlock (experimental). Customers follow ResolveCustomerUnlocked.
            bool unlocked = role == "customer"
                ? ResolveCustomerUnlocked(def)
                : def.Relationships?.Unlocked ?? def.Contact?.Visible ?? true;
            builder.WithRelationshipDefaults(r =>
            {
                r.SetUnlocked(unlocked);
                var rel = def.Relationships;
                if (rel == null) return;
                if (rel.Delta.HasValue) r.WithDelta(rel.Delta.Value);
                if (!string.IsNullOrWhiteSpace(rel.UnlockType))
                {
                    if (Util.Parse.TryParseEnum(rel.UnlockType, out NPCRelationship.UnlockType ut)) r.SetUnlockType(ut);
                    else Core.Log?.Warning($"'{def.Id}': unknown relationships.unlockType '{rel.UnlockType}' - ignored.");
                }
                if (rel.Connections != null && rel.Connections.Count > 0)
                    r.WithConnectionsById(rel.Connections);
            });

            if (role == "dealer") ApplyDealer(builder, def);
            else if (role == "customer") ApplyCustomer(builder, def);

            ApplyInventory(builder, def);

            var scheduleSpecs = Spawn.ScheduleSpecFactory.Build(def);
            if (scheduleSpecs.Count > 0)
                builder.WithSchedule(scheduleSpecs);

            if (def.Spawn?.Position != null)
            {
                Quaternion rot = def.Spawn.RotationY.HasValue
                    ? Quaternion.Euler(0f, def.Spawn.RotationY.Value, 0f)
                    : Quaternion.identity;
                builder.WithSpawnPosition(def.Spawn.Position.Value, rot);
            }
        }

        /// <summary>
        /// Effective economy role of a definition: a dealer{} block wins over a customer{} block, which wins
        /// over the legacy behavior.conversation shorthand. Returns "dealer", "customer" or null.
        /// </summary>
        internal static string ResolveRole(NpcDef def)
        {
            if (def == null) return null;
            string legacy = def.Behavior?.Conversation;
            bool legacyDealer = string.Equals(legacy, "dealer", StringComparison.OrdinalIgnoreCase);
            bool legacyCustomer = string.Equals(legacy, "customer", StringComparison.OrdinalIgnoreCase);

            if (def.Dealer != null)
            {
                if (def.Customer != null || legacyCustomer)
                    Core.Log?.Warning($"'{def.Id}': has both dealer and customer data - dealer wins (an NPC can only be one).");
                return "dealer";
            }
            if (def.Customer != null)
            {
                if (legacyDealer)
                    Core.Log?.Warning($"'{def.Id}': customer{{}} block contradicts behavior.conversation=\"dealer\" - customer wins.");
                return "customer";
            }
            if (legacyDealer) return "dealer";
            if (legacyCustomer) return "customer";
            return null;
        }

        /// <summary>
        /// Start state of a customer. Vanilla customers start locked and open through a free sample or a
        /// recommendation, and both need an unlocked connection (Customer.SampleOptionValid ->
        /// RelationData.IsMutuallyKnown). A locked customer without connections could never be opened, so it
        /// starts locked only when the pack lists connections. relationships.unlocked always wins.
        /// </summary>
        internal static bool ResolveCustomerUnlocked(NpcDef def)
        {
            if (def.Relationships?.Unlocked is bool explicitUnlocked) return explicitUnlocked;
            bool hasConnections = def.Relationships?.Connections != null && def.Relationships.Connections.Count > 0;
            if (!hasConnections)
                Core.Log?.Msg($"'{def.Id}': customer has no relationships.connections - starts unlocked, since a locked customer is only opened through a connection.");
            return !hasConnections;
        }

        private static void ApplyCustomer(NPCPrefabBuilder builder, NpcDef def)
        {
            builder.WithCustomerDefaults(c =>
            {
                var cu = def.Customer;

                // Every drug type needs an affinity entry: the vanilla handover panel reads
                // Customer.GetOrderedDrugTypes()[0], and an empty list throws there, which leaves the NPC stuck in
                // its dialogue behaviour facing the player. WithAffinities seeds all types at neutral first.
                var entries = new List<(string, float)>();
                if (cu?.Affinities != null)
                    foreach (var kv in cu.Affinities) entries.Add((kv.Key, kv.Value));
                c.WithAffinities(entries);

                if (cu == null) return;
                if (cu.Spending != null) c.WithSpending(cu.Spending.Min, cu.Spending.Max);
                if (cu.OrdersPerWeek != null) c.WithOrdersPerWeek((int)cu.OrdersPerWeek.Min, (int)cu.OrdersPerWeek.Max);
                if (!string.IsNullOrWhiteSpace(cu.PreferredOrderDay)) c.WithPreferredOrderDay(cu.PreferredOrderDay);
                if (cu.OrderTime.HasValue) c.WithOrderTime(cu.OrderTime.Value);
                if (!string.IsNullOrWhiteSpace(cu.Standards)) c.WithStandards(cu.Standards);
                if (cu.AllowDirectApproach.HasValue) c.AllowDirectApproach(cu.AllowDirectApproach.Value);
                if (cu.GuaranteeFirstSample.HasValue) c.GuaranteeFirstSample(cu.GuaranteeFirstSample.Value);
                if (cu.MutualRelationRequirement != null)
                    c.WithMutualRelationRequirement(cu.MutualRelationRequirement.Min, cu.MutualRelationRequirement.Max);
                if (cu.CallPoliceChance.HasValue) c.WithCallPoliceChance(cu.CallPoliceChance.Value);
                if (cu.DependenceBase.HasValue) c.WithDependence(cu.DependenceBase.Value, cu.DependenceMultiplier ?? 1f);
                if (cu.PreferredProperties != null && cu.PreferredProperties.Count > 0)
                    c.WithPreferredPropertiesById(cu.PreferredProperties.ToArray());
            });
        }

        private static void ApplyDealer(NPCPrefabBuilder builder, NpcDef def)
        {
            // The dealer ROLE comes from PersonnelNpc.IsDealer (base-prefab choice); dealer DATA is only
            // registered when the block sets something. An empty registration would make S1API resolve its
            // default home name ("Home") and warn about it for every such NPC.
            var de = def.Dealer;
            bool hasData = de != null && (de.Type != null || de.Cut.HasValue || de.SigningFee.HasValue ||
                de.Home != null || de.CompletedDealsVariable != null ||
                de.AllowInsufficientQuality.HasValue || de.AllowExcessQuality.HasValue);
            if (!hasData) return;

            builder.WithDealerDefaults(d =>
            {
                if (de == null) return;
                if (!string.IsNullOrWhiteSpace(de.Type))
                {
                    // "player"/"cartel" are friendlier than the enum names; both spellings are accepted.
                    string type = de.Type;
                    if (string.Equals(type, "player", StringComparison.OrdinalIgnoreCase)) type = "PlayerDealer";
                    else if (string.Equals(type, "cartel", StringComparison.OrdinalIgnoreCase)) type = "CartelDealer";
                    if (Util.Parse.TryParseEnum(type, out S1API.Economy.DealerType dt)) d.WithDealerType(dt);
                    else Core.Log?.Warning($"'{def.Id}': unknown dealer.type '{de.Type}' - ignored.");
                }
                if (de.Cut.HasValue) d.WithCut(de.Cut.Value);
                if (de.SigningFee.HasValue) d.WithSigningFee(de.SigningFee.Value);
                if (!string.IsNullOrWhiteSpace(de.Home)) d.WithHomeName(de.Home);
                if (!string.IsNullOrWhiteSpace(de.CompletedDealsVariable)) d.WithCompletedDealsVariable(de.CompletedDealsVariable);
                if (de.AllowInsufficientQuality.HasValue) d.AllowInsufficientQuality(de.AllowInsufficientQuality.Value);
                if (de.AllowExcessQuality.HasValue) d.AllowExcessQuality(de.AllowExcessQuality.Value);
            });
        }

        // Vanilla enables the >50m billboard impostor unconditionally, and runtime-built AvatarSettings
        // carry no impostor texture - without one, distant custom NPCs render an empty billboard. The
        // impostor builder API only exists in newer S1API builds, so this is best-effort via reflection:
        // present -> pick a deterministic impostor (same on all co-op peers), absent -> silently skip.
        private static System.Reflection.MethodInfo _randomImpostor;
        private static bool _randomImpostorProbed;

        private static void ApplyImpostorIfSupported(NPCPrefabBuilder.AvatarDefaultsBuilder ab, NpcDef def)
        {
            try
            {
                if (!_randomImpostorProbed)
                {
                    _randomImpostorProbed = true;
                    _randomImpostor = typeof(NPCPrefabBuilder.AvatarDefaultsBuilder)
                        .GetMethod("WithRandomImpostor", new[] { typeof(int), typeof(string[]) });
                }
                _randomImpostor?.Invoke(ab, new object[] { Util.Parse.StableHash(def.Id), Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                Core.Log?.Warning($"'{def.Id}': setting an impostor failed ({ex.Message}) - distant billboard may be blank.");
            }
        }

        private static void ApplyInventory(NPCPrefabBuilder builder, NpcDef def)
        {
            var inv = def.Inventory;
            if (inv == null) return;
            builder.WithInventoryDefaults(i =>
            {
                if (inv.Cash != null) i.WithRandomCash((int)inv.Cash.Min, (int)inv.Cash.Max);
                if (inv.Items != null)
                    foreach (var item in inv.Items)
                        for (int n = 0; n < item.Quantity; n++)
                            i.WithStartupItem(item.Id);
                if (inv.ClearEachNight.HasValue) i.WithClearInventoryEachNight(inv.ClearEachNight.Value);
            });
        }

        /// <summary>
        /// Adds a map marker (the game's own <c>NPCPoI</c>) to a live custom NPC so players can find it on the
        /// phone map. Client-local and cosmetic - no economy role, no networking. Safe to call once the NPC
        /// object exists (e.g. from <see cref="PersonnelNpc"/>'s creation hook); no-ops if the map manager or
        /// NPC component is not ready.
        /// </summary>
        /// <param name="npcRoot">The NPC's root object, the one that carries the game's <c>NPC</c> component, e.g. <c>gameObject</c> inside the NPC class.</param>
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
