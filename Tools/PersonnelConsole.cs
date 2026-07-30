using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using MelonLoader.Utils;
using Personnel.Model;
using Personnel.Registration;
using UnityEngine;
using Il2CppScheduleOne.DevUtilities;                 // Singleton<T>
using Il2CppScheduleOne.PlayerScripts;                // Player.Local
using Map = Il2CppScheduleOne.Map.Map;
using NotificationsManager = Il2CppScheduleOne.UI.NotificationsManager;

namespace Personnel.Tools
{
    /// <summary>
    /// Dev-console bridge for pack authors, namespaced <c>personnel ...</c>. The game has no coordinate readout,
    /// so authoring a schedule used to mean guessing numbers or digging them out of a save file; these commands
    /// hand back the spot you are standing on, already shaped like the manifest field that wants it.
    ///
    /// Shipped in Release on purpose: this is player-facing tooling for people writing packs, not a test harness.
    /// Output goes three ways because the game's console has no output pane - the MelonLoader log keeps the full
    /// text, the system clipboard holds the pasteable JSON, and a game notification confirms the command landed.
    ///
    /// Both <c>Console.SubmitCommand</c> overloads are patched (string + List): the console UI and scripted
    /// submitters enter through different ones depending on build, so catching both is the reliable path. The
    /// string body calls the list body, so a single submission can fire both prefixes - dispatch dedupes per
    /// frame+signature to keep side effects (route append) from running twice.
    /// </summary>
    internal static class PersonnelConsole
    {
        private const string Prefix = "personnel";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static int _lastFrame = -1;
        private static string _lastSig = "";

        /// <summary>Collected route steps, kept next to the packs so a session's work survives a crash.</summary>
        private static string RoutePath => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "route.json");

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            string[] p = new string[args.Count];
            for (int i = 0; i < args.Count; i++) p[i] = args[i];
            return Dispatch(p);
        }

        /// <summary>True = ours, swallow it. False = let the game handle the command.</summary>
        private static bool Dispatch(string[] p)
        {
            if (p.Length == 0 || !p[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string sig = string.Join(" ", p);
            int frame = Time.frameCount;
            if (frame == _lastFrame && sig == _lastSig) return true;   // second overload for the same submission
            _lastFrame = frame; _lastSig = sig;

            string cmd = p.Length > 1 ? p[1].ToLowerInvariant() : "help";
            try
            {
                switch (cmd)
                {
                    case "help": Help(); break;
                    case "pos": Pos(Arg(p, 2)); break;
                    case "spawn": SpawnBlock(); break;
                    case "route": Route(Arg(p, 2)); break;
                    case "npcs": Npcs(Arg(p, 2)); break;
                    default:
                        Log($"unknown command '{cmd}'. Try: personnel help");
                        break;
                }
            }
            catch (Exception e)
            {
                Log("command failed: " + e.Message);
            }
            return true;
        }

        private static void Help()
        {
            Log("commands (results are copied to your clipboard and written to this log):");
            Log("  personnel pos [HH:MM]   position you are standing on - with a time it becomes a walkTo action");
            Log("  personnel spawn         the same spot as a spawn block (x/y/z/rotationY/region)");
            Log("  personnel route HH:MM   append a walkTo step to " + RoutePath);
            Log("  personnel route show    print the collected steps");
            Log("  personnel route clear   start a new route");
            Log("  personnel npcs [filter] list loaded Personnel NPCs, and where the physical ones are right now");
            Notify("Personnel", "Command list written to the MelonLoader log.");
        }

        // ---- position ----------------------------------------------------------------------------------

        private static void Pos(string timeArg)
        {
            if (!TryGetPlayer(out Vector3 pos, out float yaw)) return;

            if (string.IsNullOrEmpty(timeArg))
            {
                string coords = Vec(pos);
                Log("position " + coords + "  (facing " + Num(yaw) + " degrees)");
                Log("  paste into any action that takes a position: \"position\": " + coords);
                Copy(coords);
                Notify("Position copied", Plain(pos));
                return;
            }

            if (!TryNormalizeTime(timeArg, out string time))
            {
                Log($"'{timeArg}' is not a time. Use HH:MM, e.g. personnel pos 07:30");
                return;
            }

            string action = WalkTo(time, pos);
            Log("walkTo step at " + time + ":");
            Log("  " + action);
            Copy(action);
            Notify("walkTo copied", time + "  " + Plain(pos));
        }

        private static void SpawnBlock()
        {
            if (!TryGetPlayer(out Vector3 pos, out float yaw)) return;

            string region = RegionAt(pos);
            var sb = new StringBuilder();
            sb.Append("\"spawn\": { \"x\": ").Append(Num(pos.x))
              .Append(", \"y\": ").Append(Num(pos.y))
              .Append(", \"z\": ").Append(Num(pos.z))
              .Append(", \"rotationY\": ").Append(Num(yaw));
            if (region != null) sb.Append(", \"region\": \"").Append(region).Append('"');
            sb.Append(", \"physical\": true }");

            string block = sb.ToString();
            Log("spawn block:");
            Log("  " + block);
            Copy(block);
            Notify("Spawn block copied", Plain(pos) + (region != null ? "  " + region : ""));
        }

        // ---- route -------------------------------------------------------------------------------------

        private static void Route(string arg)
        {
            if (string.Equals(arg, "show", StringComparison.OrdinalIgnoreCase)) { RouteShow(); return; }
            if (string.Equals(arg, "clear", StringComparison.OrdinalIgnoreCase)) { RouteClear(); return; }

            if (string.IsNullOrEmpty(arg))
            {
                Log("personnel route HH:MM  (or: personnel route show | personnel route clear)");
                return;
            }
            if (!TryNormalizeTime(arg, out string time))
            {
                Log($"'{arg}' is not a time. Use HH:MM, e.g. personnel route 07:30");
                return;
            }
            if (!TryGetPlayer(out Vector3 pos, out _)) return;

            string action = WalkTo(time, pos);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RoutePath));
                File.AppendAllText(RoutePath, action + "," + Environment.NewLine);
            }
            catch (Exception e)
            {
                Log("could not write " + RoutePath + ": " + e.Message);
                return;
            }

            int steps = CountRouteSteps();
            Log($"route step {steps} at {time}: {action}");
            Log("  file: " + RoutePath);
            Notify("Route step " + steps, time + "  " + Plain(pos));
        }

        private static void RouteShow()
        {
            string[] lines = ReadRoute();
            if (lines.Length == 0)
            {
                Log("no route steps yet - walk somewhere and run: personnel route 07:30");
                Notify("Personnel", "Route is empty.");
                return;
            }

            // Drop the trailing comma of the last entry so the block pastes into "schedule": [ ... ] as-is.
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(lines[i].TrimEnd());
                if (i < lines.Length - 1) sb.Append(',');
                sb.Append(Environment.NewLine);
            }
            string block = sb.ToString();

            Log($"route ({lines.Length} step(s)) - paste into \"schedule\": [ ... ]:");
            foreach (string line in block.Split('\n')) Log("  " + line.TrimEnd());
            Copy(block);
            Notify("Route copied", lines.Length + " step(s)");
        }

        private static void RouteClear()
        {
            try { if (File.Exists(RoutePath)) File.Delete(RoutePath); }
            catch (Exception e) { Log("could not clear " + RoutePath + ": " + e.Message); return; }
            Log("route cleared.");
            Notify("Personnel", "Route cleared.");
        }

        private static string[] ReadRoute()
        {
            try
            {
                if (!File.Exists(RoutePath)) return Array.Empty<string>();
                var kept = new List<string>();
                foreach (string line in File.ReadAllLines(RoutePath))
                {
                    string t = line.Trim().TrimEnd(',');
                    if (t.Length > 0) kept.Add(t);
                }
                return kept.ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        private static int CountRouteSteps() => ReadRoute().Length;

        // ---- roster ------------------------------------------------------------------------------------

        private static void Npcs(string filter)
        {
            IReadOnlyList<NpcDef> defs = NpcRegistry.AllDefs;
            if (defs == null || defs.Count == 0)
            {
                Log("no NPC definitions loaded. Packs live in " + Content.PackLoader.PacksRoot);
                Notify("Personnel", "No NPC definitions loaded.");
                return;
            }

            var live = LivePositions();
            int shown = 0;
            Log($"{defs.Count} definition(s) loaded:");
            foreach (NpcDef def in defs)
            {
                if (def == null) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    (def.Id == null || def.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                bool hasSchedule = def.Schedule != null && def.Schedule.Count > 0;
                bool physical = def.Spawn?.Physical ?? hasSchedule;
                string where = physical && live.TryGetValue(def.Id ?? "", out Vector3 p) ? "  at " + Plain(p) : "";
                Log($"  {def.Id} ({def.Source}) {(physical ? "physical" : "contact-only")}" +
                    $"{(hasSchedule ? ", " + def.Schedule.Count + " schedule step(s)" : "")}{where}");
                shown++;
            }
            if (shown == 0) Log("  (nothing matched '" + filter + "')");
            Notify("Personnel", shown + " of " + defs.Count + " definition(s) listed.");
        }

        /// <summary>Current world position per NPC id, for the definitions that actually spawned.</summary>
        private static Dictionary<string, Vector3> LivePositions()
        {
            var map = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var all = S1API.Entities.NPC.All;
                if (all == null) return map;
                foreach (var npc in all)
                {
                    if (npc == null) continue;
                    try
                    {
                        string id = npc.ID;
                        if (!string.IsNullOrEmpty(id)) map[id] = npc.Position;
                    }
                    catch { }
                }
            }
            catch { }
            return map;
        }

        // ---- helpers -----------------------------------------------------------------------------------

        private static bool TryGetPlayer(out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            Player local = null;
            try { local = Player.Local; } catch { }
            if (local == null || local.transform == null)
            {
                Log("no local player - load into a save first.");
                return false;
            }
            pos = local.transform.position;
            yaw = local.transform.eulerAngles.y;      // the value the game itself persists as PlayerData.Rotation
            return true;
        }

        /// <summary>Region name matching the manifest's <c>spawn.region</c>, or null when the map is unavailable.</summary>
        private static string RegionAt(Vector3 pos)
        {
            try
            {
                Map map = Singleton<Map>.Instance;
                return map == null ? null : map.GetRegionFromPosition(pos).ToString();
            }
            catch { return null; }
        }

        private static string WalkTo(string time, Vector3 pos)
            => "{ \"type\": \"walkTo\", \"time\": \"" + time + "\", \"position\": " + Vec(pos) + ", \"warpIfSkipped\": true }";

        /// <summary>Accepts 7:30, 07:30 and 0730, and returns the HH:MM form the pack loader parses.</summary>
        private static bool TryNormalizeTime(string raw, out string time)
        {
            time = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim();
            int h, m;

            int colon = s.IndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(s.Substring(0, colon), NumberStyles.Integer, Inv, out h)) return false;
                if (!int.TryParse(s.Substring(colon + 1), NumberStyles.Integer, Inv, out m)) return false;
            }
            else
            {
                if (!int.TryParse(s, NumberStyles.Integer, Inv, out int hhmm)) return false;
                h = hhmm / 100; m = hhmm % 100;
            }

            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            time = h.ToString("00", Inv) + ":" + m.ToString("00", Inv);
            return true;
        }

        private static string Num(float f) => f.ToString("0.##", Inv);

        private static string Vec(Vector3 v) => "[" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + "]";

        /// <summary>Short form for the notification toast, which has no room for JSON.</summary>
        private static string Plain(Vector3 v) => Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z);

        private static string Arg(string[] p, int i) => p.Length > i ? p[i] : null;

        private static void Copy(string text)
        {
            try { GUIUtility.systemCopyBuffer = text; }
            catch (Exception e) { Log("clipboard unavailable (" + e.Message + ") - copy it from this log instead."); }
        }

        private static void Notify(string title, string subtitle)
        {
            try
            {
                NotificationsManager n = Singleton<NotificationsManager>.Instance;
                if (n != null) n.SendNotification(title, subtitle, null, 5f, false);
            }
            catch { }
        }

        // The MelonLogger already stamps every line with [Personnel], so no second prefix here.
        private static void Log(string msg) => Core.Log?.Msg(msg);
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), "SubmitCommand", new Type[] { typeof(string) })]
    internal static class Personnel_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !PersonnelConsole.TryHandle(args); } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), "SubmitCommand", new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Personnel_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !PersonnelConsole.TryHandle(args); } catch { return true; }
        }
    }
}
