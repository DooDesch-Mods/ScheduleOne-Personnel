using System;
using System.Collections.Generic;
using System.Text;
using Il2CppScheduleOne.AvatarFramework.Animation;
using Il2CppScheduleOne.Map;
using Personnel.Model;
using UnityEngine;

namespace Personnel.Spawn
{
    /// <summary>
    /// Names every sit and stay-in-building step whose scene reference does not exist. S1API skips such a step with a
    /// Unity warning that names neither the pack NPC id nor the step time; the NPC then simply never sits or enters.
    /// The lookups mirror S1API: a seat set by full path or path suffix (case-insensitive, inactive objects included),
    /// then by object name; a building by <c>NPCEnterableBuilding.BuildingName</c> (case-insensitive).
    /// The scene objects only exist in Main, so this runs per NPC instance; the scene lists are cached per frame.
    /// </summary>
    internal static class ScheduleRefGuard
    {
        private static int _cacheFrame = -1;
        private static List<string> _seatPaths;
        private static HashSet<string> _seatNames;
        private static HashSet<string> _buildings;

        public static void Apply(NpcDef def)
        {
            if (def?.Schedule == null || def.Schedule.Count == 0) return;
            Refresh();

            int checkedSteps = 0, missing = 0;
            foreach (NpcScheduleAction a in def.Schedule)
            {
                if (a == null || string.IsNullOrEmpty(a.Type)) continue;
                string type = a.Type.Trim().ToLowerInvariant();
                if (type == "sit")
                {
                    checkedSteps++;
                    if (SeatExists(a.SeatSetPath, a.SeatSet)) continue;
                    missing++;
                    string what = !string.IsNullOrWhiteSpace(a.SeatSetPath) ? $"seatSetPath '{a.SeatSetPath}'" : $"seatSet '{a.SeatSet}'";
                    Core.Log?.Warning($"'{def.Id}' ({def.DisplayName}) schedule action 'sit' at {At(a)}: {what} matches no seat set in the scene - the NPC skips this step.");
                }
                else if (type == "stayinbuilding")
                {
                    checkedSteps++;
                    if (string.IsNullOrWhiteSpace(a.Building) || _buildings.Contains(a.Building.Trim())) continue;
                    missing++;
                    Core.Log?.Warning($"'{def.Id}' ({def.DisplayName}) schedule action 'stayInBuilding' at {At(a)}: building '{a.Building}' is not in the scene - the NPC skips this step.");
                }
            }
            if (missing > 0) Core.Log?.Warning($"'{def.Id}': {missing} of {checkedSteps} sit/stay step(s) point at a seat or building that does not exist.");
            else Core.Log?.Msg($"'{def.Id}': {checkedSteps} sit/stay step(s) checked, every seat and building found.");
        }

        private static string At(NpcScheduleAction a) => a.Time >= 0 ? a.Time.ToString("D4") : "(no time)";

        private static bool SeatExists(string path, string name)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                string p = path.Trim().Replace('\\', '/');
                foreach (string full in _seatPaths)
                    if (full.EndsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return !string.IsNullOrWhiteSpace(name) && _seatNames.Contains(name.Trim());
        }

        private static void Refresh()
        {
            if (_cacheFrame == Time.frameCount && _seatPaths != null) return;
            _cacheFrame = Time.frameCount;
            _seatPaths = new List<string>();
            _seatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _buildings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AvatarSeatSet s in UnityEngine.Object.FindObjectsOfType<AvatarSeatSet>(true))
            {
                if (s == null) continue;
                _seatPaths.Add(PathOf(s.transform));
                _seatNames.Add(s.gameObject.name);
            }
            foreach (NPCEnterableBuilding b in UnityEngine.Object.FindObjectsOfType<NPCEnterableBuilding>(true))
                if (b != null && !string.IsNullOrEmpty(b.BuildingName)) _buildings.Add(b.BuildingName);

            if (_seatPaths.Count == 0 || _buildings.Count == 0)
                Core.Log?.Warning($"[schedule check] the scene lists {_seatPaths.Count} seat set(s) and {_buildings.Count} building(s); seat and building warnings below may be wrong.");
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (Transform cur = t; cur != null; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++) { if (i > 0) sb.Append('/'); sb.Append(parts[i]); }
            return sb.ToString();
        }
    }
}
