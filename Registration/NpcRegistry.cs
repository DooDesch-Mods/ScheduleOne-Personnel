using System;
using System.Collections.Generic;
using Personnel.Content;
using Personnel.Model;

namespace Personnel.Registration
{
    /// <summary>
    /// Master list of NPC definitions and their de-duplication. Pack-sourced defs (a non-null <see cref="NpcDef.PackDir"/>)
    /// are reloadable; API-registered defs persist across reloads.
    /// </summary>
    internal static class NpcRegistry
    {
        private static readonly List<NpcDef> _all = new List<NpcDef>();
        private static readonly HashSet<string> _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<NpcDef> AllDefs => _all;

        /// <summary>Fired after a pack reload completes.</summary>
        public static event Action OnReloaded;

        /// <summary>Registers a definition (de-duplicated by Source/Id). Returns false if it was a duplicate/invalid.</summary>
        public static bool Add(NpcDef def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Id)) return false;
            if (string.IsNullOrWhiteSpace(def.Source)) def.Source = "API";
            if (!_keys.Add(def.Key)) return false;
            _all.Add(def);
            return true;
        }

        public static int AddRange(IEnumerable<NpcDef> defs)
        {
            int n = 0;
            if (defs == null) return 0;
            foreach (NpcDef d in defs) if (Add(d)) n++;
            return n;
        }

        public static bool TryGet(string id, out NpcDef def)
        {
            def = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            foreach (NpcDef d in _all)
            {
                if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) { def = d; return true; }
            }
            return false;
        }

        /// <summary>Scan the packs folder and add every definition found. Returns the number newly added.</summary>
        public static int LoadPacks() => AddRange(PackLoader.LoadAll());

        /// <summary>Drop all pack-sourced defs, rescan the packs folder, and fire <see cref="OnReloaded"/>.</summary>
        public static void Reload()
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                if (_all[i].PackDir != null)
                {
                    _keys.Remove(_all[i].Key);
                    _all.RemoveAt(i);
                }
            }
            LoadPacks();
            try { OnReloaded?.Invoke(); }
            catch (Exception e) { Core.Log?.Warning("OnReloaded handler threw: " + e.Message); }
        }
    }
}
