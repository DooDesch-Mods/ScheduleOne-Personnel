using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using Personnel.Model;
using UnityEngine;
using S1DialogueChain = Il2CppScheduleOne.Dialogue.DialogueChain;
using S1DialogueHandler = Il2CppScheduleOne.Dialogue.DialogueHandler;
using S1DialogueModule = Il2CppScheduleOne.Dialogue.DialogueModule;
using S1EDialogueModule = Il2CppScheduleOne.Dialogue.EDialogueModule;
using S1Entry = Il2CppScheduleOne.Dialogue.Entry;

namespace Personnel.Spawn
{
    /// <summary>
    /// Puts an NPC's own lines in front of the vanilla ones. The game reads a line from the first dialogue module of
    /// a type in the handler's RuntimeModules, and a key missing from that module has no fallback. So for every
    /// module type the NPC overrides, a new module is built from a copy of the module that would have answered,
    /// with the own keys replaced, and inserted at the front. Shared database assets are never changed.
    /// </summary>
    internal static class CustomLines
    {
        // Handlers that already carry our modules, so a second install only happens when the game rebuilt the list.
        private static readonly Dictionary<IntPtr, List<S1DialogueModule>> Installed = new Dictionary<IntPtr, List<S1DialogueModule>>();

        // Database copies made here; they belong to one NPC and are never copied again.
        private static readonly HashSet<IntPtr> OwnDatabases = new HashSet<IntPtr>();

        internal static void Apply(GameObject npc, NpcDef def)
        {
            if (def?.Lines == null || def.Lines.Entries.Count == 0) return;
            MelonCoroutines.Start(ApplyWhenReady(npc, def));
        }

        // The dialogue handler fills RuntimeModules in NPC initialisation, which can run after OnCreated. Wait for
        // it, install, then look again once later in case the game re-initialised the NPC.
        private static IEnumerator ApplyWhenReady(GameObject npc, NpcDef def)
        {
            float until = Time.realtimeSinceStartup + 30f;
            S1DialogueHandler handler = null;
            while (Time.realtimeSinceStartup < until)
            {
                if (npc == null) { Core.Log?.Warning($"'{def.Id}': NPC object gone before its own lines were set."); yield break; }
                handler = npc.GetComponentInChildren<S1DialogueHandler>(true);
                if (handler != null && handler.RuntimeModules != null && handler.RuntimeModules.Count > 0) break;
                handler = null;
                yield return new WaitForSeconds(0.5f);
            }
            if (handler == null) { Core.Log?.Error($"'{def.Id}': no initialised dialogue handler after 30 s - own lines not set."); yield break; }
            Install(handler, def);

            yield return new WaitForSeconds(10f);
            if (handler != null && !StillInFront(handler))
            {
                Core.Log?.Msg($"'{def.Id}': dialogue modules were rebuilt by the game - own lines set again.");
                Install(handler, def);
            }
        }

        private static void Rebind(Il2CppScheduleOne.Dialogue.DialogueDatabase shared, S1DialogueHandler mine, string id)
        {
            foreach (var n in Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry)
            {
                var h = n?.DialogueHandler;
                if (h == null || h.Pointer == mine.Pointer || h.Database == null || h.Database.Pointer != shared.Pointer) continue;
                shared.Initialize(h);
                Core.Log?.Msg($"'{id}': shared dialogue database '{shared.name}' now bound to '{n.ID}'.");
                return;
            }
            Core.Log?.Msg($"'{id}': no other NPC uses dialogue database '{shared.name}' - left as it is.");
        }

        private static bool StillInFront(S1DialogueHandler handler)
        {
            if (!Installed.TryGetValue(handler.Pointer, out var mine)) return false;
            var rm = handler.RuntimeModules;
            foreach (var m in mine)
                if (m == null || !rm.Contains(m)) return false;
            return true;
        }

        private static void Install(S1DialogueHandler handler, NpcDef def)
        {
            var byModule = new Dictionary<string, List<(string Key, List<List<string>> Variants)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in def.Lines.Entries)
            {
                if (!byModule.TryGetValue(e.Module, out var list)) byModule[e.Module] = list = new List<(string, List<List<string>>)>();
                list.Add((e.Key, e.Variants));
            }

            // Customers share one dialogue database asset, and a database resolves modules through the handler
            // that bound it last. Without an own copy the NPC's modules would either be ignored or answer for every
            // NPC on that asset. The copy is bound to this handler only.
            try
            {
                var shared = handler.Database;
                if (shared != null && !OwnDatabases.Contains(shared.Pointer))
                {
                    var own = UnityEngine.Object.Instantiate(shared);
                    own.name = shared.name + " (" + def.Id + ")";
                    own.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    own.Initialize(handler);
                    handler._Database_k__BackingField = own;
                    OwnDatabases.Add(own.Pointer);
                    // If the shared asset was bound to this handler, the NPCs still on it would read this NPC's
                    // modules. Bind it to another handler that uses it (all of them carry the same vanilla modules).
                    if (shared.handler != null && shared.handler.Pointer == handler.Pointer) Rebind(shared, handler, def.Id);
                }
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"'{def.Id}': own dialogue database could not be made ({ex.GetType().Name}: {ex.Message}) - own lines not set.");
                return;
            }

            var mine = new List<S1DialogueModule>();
            int keys = 0;
            foreach (var kv in byModule)
            {
                if (!Enum.TryParse(kv.Key, true, out S1EDialogueModule type))
                {
                    Core.Log?.Warning($"'{def.Id}': unknown dialogue module '{kv.Key}' - {kv.Value.Count} line(s) skipped.");
                    continue;
                }
                try
                {
                    var module = Build(handler, type, kv.Value);
                    handler.RuntimeModules.Insert(0, module);
                    mine.Add(module);
                    keys += kv.Value.Count;
                }
                catch (Exception ex)
                {
                    Core.Log?.Error($"'{def.Id}': own {type} lines could not be set ({ex.GetType().Name}: {ex.Message}).");
                }
            }
            Installed[handler.Pointer] = mine;
            if (mine.Count > 0) Core.Log?.Msg($"'{def.Id}': own lines set ({keys} key(s) in {mine.Count} module(s)).");
        }

        private static S1DialogueModule Build(S1DialogueHandler handler, S1EDialogueModule type, List<(string Key, List<List<string>> Variants)> own)
        {
            // The module that answers today: the first of this type in the handler, else the database's fallback.
            S1DialogueModule current = null;
            foreach (var m in handler.RuntimeModules)
                if (m != null && m.ModuleType == type) { current = m; break; }
            if (current == null && handler.Database != null) current = handler.Database.GetModule(type);

            var replaced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in own) replaced.Add(o.Key);

            var module = handler.gameObject.AddComponent<S1DialogueModule>();
            module.ModuleType = type;
            var entries = new Il2CppSystem.Collections.Generic.List<S1Entry>();
            if (current?.Entries != null)
                foreach (var e in current.Entries)
                    if (!replaced.Contains(e.Key)) entries.Add(e);
            foreach (var o in own)
            {
                var chains = new Il2CppReferenceArray<S1DialogueChain>(o.Variants.Count);
                for (int i = 0; i < o.Variants.Count; i++)
                    chains[i] = new S1DialogueChain { Lines = new Il2CppStringArray(o.Variants[i].ToArray()) };
                entries.Add(new S1Entry { Key = o.Key, Chains = chains });
            }
            module.Entries = entries;
            return module;
        }
    }
}
