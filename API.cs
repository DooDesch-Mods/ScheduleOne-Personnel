using System;
using System.Collections.Generic;
using Il2CppScheduleOne.AvatarFramework;
using Personnel.Appearance;
using Personnel.Model;
using Personnel.Registration;
using S1API.Entities;
using UnityEngine;

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
            if (def.Spawn?.Position != null)
                builder.WithSpawnPosition(def.Spawn.Position.Value, Quaternion.identity);
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
