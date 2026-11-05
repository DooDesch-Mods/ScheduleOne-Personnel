#if DEBUG
using System;
using Personnel.Cards;
using Personnel.Replacements;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1Dealer = Il2CppScheduleOne.Economy.Dealer;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Tools
{
    /// <summary>
    /// <c>personnel replace ...</c>: reads and drives vanilla NPC replacements for test scripts. Every result is one
    /// <c>[replace-probe]</c> line with key=value pairs.
    /// </summary>
    internal static class ReplaceProbe
    {
        public static void Run(string sub, string a, string b)
        {
            switch ((sub ?? "list").ToLowerInvariant())
            {
                case "list":
                    Line($"registered={ReplacementRegistry.All.Count} applied={VanillaReplacer.Applied.Count}");
                    foreach (var kv in ReplacementRegistry.All)
                        Line($"target={kv.Key} def={kv.Value.Key} applied={VanillaReplacer.Applied.ContainsKey(kv.Key)}");
                    break;
                case "probe":
                    Probe(a);
                    break;
                case "apply":
                    Line($"applied={VanillaReplacer.ApplyAll()}");
                    break;
                case "restore":
                    Line($"target={a} restored={VanillaReplacer.Restore(a)}");
                    break;
                case "set":
                    // set <card code> <vanilla id|none>: what the insert dialog does; applies after a restart.
                    Line("set " + CardsApi.SetReplacement(a, string.Equals(b, "none", StringComparison.OrdinalIgnoreCase) ? null : b).Replace("\r", "").Replace("\n", " "));
                    break;
                case "blocked":
                    Line($"target={a} blocked={ReplacementRules.BlockReason(VanillaReplacer.Find(a)) ?? ReplacementRules.BlockReason(a) ?? "no"}");
                    break;
                case "catalog":
                    Line($"catalog={Testing.NpcCatalog.Write()}");
                    break;
                default:
                    Core.Log?.Msg("usage: personnel replace list | probe <id> | apply | restore <id> | set <code> <id|none> | blocked <id> | catalog");
                    break;
            }
        }

        private static void Probe(string id)
        {
            S1NPC n = VanillaReplacer.Find(id);
            if (n == null) { Line($"target={id} found=false"); return; }
            var app = n.Avatar?.Appearance;
            int worn = app?._wornObjects?.Count ?? -1, naked = app?._nakedObjects?.Count ?? -1;
            string hair = "", first = "";
            if (app?._nakedObjects != null)
                for (int i = 0; i < app._nakedObjects.Count; i++)
                {
                    var o = app._nakedObjects[i];
                    if (o == null) continue;
                    if (first.Length == 0) first = o.Id;
                    if (o.TryCast<Il2CppScheduleOne.Core.Avatar.HairAvatarObject>() != null) hair = o.Id;
                }
            string wornIds = "";
            if (app?._wornObjects != null)
                for (int i = 0; i < app._wornObjects.Count; i++)
                    if (app._wornObjects[i] != null) wornIds += (wornIds.Length > 0 ? "," : "") + app._wornObjects[i].Id;
            var vo = n.VoiceOverEmitter;
            var dealer = n.TryCast<S1Dealer>()?.DealerData;
            var cd = n.GetComponent<S1Customer>()?.customerData;
            Line($"target={id} found=true name={n.FirstName}_{n.LastName} applied={VanillaReplacer.Applied.ContainsKey(id)} " +
                 $"skin={app?.AppliedNakedAppearance?.SkinColor} naked={naked} worn={worn} hair={hair} wornIds={wornIds} " +
                 $"voice={vo?._currentDatabase?.name} pitch={vo?._defaultPitch:0.##} " +
                 $"cut={(dealer != null ? dealer.SalesCutPercentage.ToString("0.##") : "-")} " +
                 $"spend={(cd != null ? cd.MinWeeklySpend.ToString("0") + "-" + cd.MaxWeeklySpend.ToString("0") : "-")} " +
                 $"day={(cd != null ? cd.PreferredOrderDay.ToString() : "-")} mugshot={n.MugshotSprite?.name}");
        }

        private static void Line(string s) => Core.Log?.Msg("[replace-probe] " + s);
    }
}
#endif
