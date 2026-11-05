using System;
using System.Collections.Generic;
using Personnel.Model;

namespace Personnel.Replacements
{
    /// <summary>
    /// Pack definitions that take over a vanilla NPC (<see cref="NpcDef.Replaces"/>), one per vanilla NPC. Packs load
    /// in folder order, so when two packs name the same vanilla NPC the first one wins on every peer alike and the
    /// later one is logged and ignored.
    /// </summary>
    internal static class ReplacementRegistry
    {
        private static readonly Dictionary<string, NpcDef> ByTarget = new Dictionary<string, NpcDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Active replacements keyed by vanilla NPC id.</summary>
        public static IReadOnlyDictionary<string, NpcDef> All => ByTarget;

        public static void Set(IEnumerable<NpcDef> defs)
        {
            ByTarget.Clear();
            foreach (NpcDef d in defs)
            {
                if (ReplacementRules.BlockReason(d.Replaces) is string why)
                {
                    Core.Log?.Warning($"Pack '{d.Source}': '{d.DisplayName}' wants to replace '{d.Replaces}', which is {why} - ignored, the vanilla NPC stays.");
                    continue;
                }
                if (ByTarget.TryGetValue(d.Replaces, out NpcDef first))
                {
                    Core.Log?.Warning($"Pack '{d.Source}': '{d.DisplayName}' also replaces '{d.Replaces}'; '{first.DisplayName}' from pack '{first.Source}' already does - ignored.");
                    continue;
                }
                ByTarget[d.Replaces] = d;
                Core.Log?.Msg($"Pack '{d.Source}': '{d.DisplayName}' replaces vanilla NPC '{d.Replaces}'.");
            }
        }

        public static bool TryGet(string vanillaId, out NpcDef def)
        {
            def = null;
            return !string.IsNullOrEmpty(vanillaId) && ByTarget.TryGetValue(vanillaId, out def);
        }
    }
}
