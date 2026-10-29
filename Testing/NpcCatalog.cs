using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using UnityEngine;
using Il2CppScheduleOne.Persistence;
using Personnel.Registration;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1Dealer = Il2CppScheduleOne.Economy.Dealer;
using S1Supplier = Il2CppScheduleOne.Economy.Supplier;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Testing
{
    /// <summary>
    /// Writes every NPC of the loaded save (vanilla, Personnel and other mods) to
    /// <c>UserData/Personify/World/npcs.json</c>, so an editor in the main menu can offer them as connections.
    /// Runs once per save load, after custom NPCs have spawned.
    /// </summary>
    public static class NpcCatalog
    {
        /// <summary>One NPC in the catalog.</summary>
        public sealed class Entry
        {
            public string id;
            public string name;
            public string region;
            /// <summary>"customer", "dealer", "supplier" or "" for everyone else.</summary>
            public string role;
            /// <summary>"vanilla", "personnel" or "mod".</summary>
            public string source;
        }

        /// <summary>File contents.</summary>
        public sealed class Catalog
        {
            public string gameVersion;
            public DateTime writtenUtc;
            public List<Entry> npcs = new List<Entry>();
        }

        /// <summary>Path of the catalog file.</summary>
        public static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "Personify", "World", "npcs.json");

        private static bool _pending;

        internal static void OnSceneInitialized(string sceneName)
        {
            if (sceneName != "Main" || _pending) return;
            _pending = true;
            MelonCoroutines.Start(WriteWhenSettled());
        }

        // Custom NPCs register a few seconds after the scene; the registry count holding still for 3 s means
        // every mod has spawned its NPCs.
        private static IEnumerator WriteWhenSettled()
        {
            float until = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < until && !(LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded))
                yield return new WaitForSeconds(1f);
            if (!(LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded))
            {
                Core.Log?.Warning("npc catalog: the save did not finish loading within 180 s - catalog not written.");
                _pending = false;
                yield break;
            }

            int last = -1, stable = 0;
            until = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < until && stable < 3)
            {
                yield return new WaitForSeconds(1f);
                int n = Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry?.Count ?? 0;
                stable = n == last ? stable + 1 : 0;
                last = n;
            }
            _pending = false;
            Write();
        }

        /// <summary>Reads the registry and writes the file. Returns false (and logs) on failure.</summary>
        public static bool Write()
        {
            var cat = new Catalog { gameVersion = Application.version, writtenUtc = DateTime.UtcNow };
            int skipped = 0;
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var modIds = ModNpcIds();
                foreach (S1NPC n in Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry)
                {
                    if (n == null || string.IsNullOrWhiteSpace(n.ID) || !seen.Add(n.ID)) { skipped++; continue; }
                    string name = ((n.FirstName ?? "") + " " + (n.LastName ?? "")).Trim();
                    cat.npcs.Add(new Entry
                    {
                        id = n.ID,
                        name = name.Length > 0 ? name : n.ID,
                        region = n.Region.ToString(),
                        role = n.GetComponent<S1Dealer>() != null ? "dealer"
                             : n.GetComponent<S1Supplier>() != null ? "supplier"
                             : n.GetComponent<S1Customer>() != null ? "customer" : "",
                        source = NpcRegistry.TryGet(n.ID, out _) ? "personnel" : modIds.Contains(n.ID) ? "mod" : "vanilla",
                    });
                }
                cat.npcs.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                Core.Log?.Error($"npc catalog: reading the NPC registry failed ({e.GetType().Name}: {e.Message}).");
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(cat, Formatting.Indented));
                Core.Log?.Msg($"npc catalog: {cat.npcs.Count} NPC(s) written to {FilePath} ({skipped} skipped: empty or duplicate id).");
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Error($"npc catalog could not be written ({e.Message}).");
                return false;
            }
        }

        // Ids of NPCs another mod added through S1API: their wrapper is a subclass of S1API's NPC.
        private static HashSet<string> ModNpcIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in S1API.Entities.NPC.All)
            {
                try
                {
                    if (w != null && w.GetType() != typeof(S1API.Entities.NPC) && !string.IsNullOrEmpty(w.ID)) ids.Add(w.ID);
                }
                catch (Exception e) { Core.Log?.Warning($"npc catalog: reading a mod NPC wrapper failed ({e.GetType().Name}) - it counts as vanilla."); }
            }
            return ids;
        }
    }
}
