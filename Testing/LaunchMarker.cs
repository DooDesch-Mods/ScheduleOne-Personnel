using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using UnityEngine;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Testing
{
    /// <summary>
    /// One-shot "test this NPC" start marker. An editor (Personify) writes <c>UserData/Personnel/launch.json</c>,
    /// restarts the game, and on the next main menu Personnel loads the named save and puts the player next to
    /// the named NPC. The marker is deleted before the load starts, so a crash during the load cannot loop.
    /// </summary>
    public static class LaunchMarker
    {
        /// <summary>Contents of the marker file.</summary>
        public sealed class Marker
        {
            /// <summary>Save slot number as the main menu shows it (1-5).</summary>
            public int saveSlot;
            /// <summary>NPC id to stand next to after the load; empty = only load.</summary>
            public string npcId;
            /// <summary>UTC time the marker was written; markers older than <see cref="MaxAge"/> are dropped.</summary>
            public DateTime createdUtc;
        }

        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
        private static bool _running;

        /// <summary>Path of the marker file.</summary>
        public static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "launch.json");

        /// <summary>Writes the marker. Returns false (and logs) when the file cannot be written.</summary>
        public static bool Write(int saveSlot, string npcId)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var m = new Marker { saveSlot = saveSlot, npcId = npcId ?? "", createdUtc = DateTime.UtcNow };
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(m, Formatting.Indented));
                Core.Log?.Msg($"launch marker written: save {saveSlot}, npc '{npcId}' -> {FilePath}");
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Error($"launch marker could not be written ({e.Message}).");
                return false;
            }
        }

        /// <summary>
        /// Starts the same executable with the same arguments and quits the game. Returns false (and keeps running)
        /// when the new process cannot start.
        /// </summary>
        public static bool RestartGame()
        {
            try
            {
                Process self = Process.GetCurrentProcess();
                string exe = self.MainModule?.FileName;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    Core.Log?.Error($"restart: own executable not found ('{exe}').");
                    return false;
                }
                string[] argv = Environment.GetCommandLineArgs();
                string args = argv.Length > 1 ? string.Join(" ", argv, 1, argv.Length - 1) : "";

                // A direct start, because MLVScan blocks a hidden PowerShell helper on the player's machine. The new
                // process inherits the Steam environment of this one.
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                };
                Process next = Process.Start(psi);
                if (next == null)
                {
                    Core.Log?.Error("restart: the new game process did not start.");
                    return false;
                }
                Core.Log?.Msg($"restart: pid {next.Id} started from pid {self.Id}: '{exe}' {args}");
                Application.Quit();
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Error($"restart failed ({e.GetType().Name}: {e.Message}).");
                return false;
            }
        }

        /// <summary>Called on every scene init; acts once per marker on the main menu.</summary>
        internal static void OnSceneInitialized(string sceneName)
        {
            if (sceneName != "Menu" || _running) return;
            if (!File.Exists(FilePath)) return;

            Marker m;
            try { m = JsonConvert.DeserializeObject<Marker>(File.ReadAllText(FilePath)); }
            catch (Exception e)
            {
                Core.Log?.Error($"launch marker unreadable ({e.Message}) - deleted.");
                TryDelete();
                return;
            }
            TryDelete();

            if (m == null || m.saveSlot <= 0)
            {
                Core.Log?.Warning("launch marker has no save slot - ignored.");
                return;
            }
            if (DateTime.UtcNow - m.createdUtc > MaxAge)
            {
                Core.Log?.Warning($"launch marker from {m.createdUtc:u} is older than {MaxAge.TotalMinutes} min - ignored.");
                return;
            }
            _running = true;
            MelonCoroutines.Start(Run(m));
        }

        private static IEnumerator Run(Marker m)
        {
            // The menu needs a few frames before LoadManager has read the save list.
            SaveInfo info = null;
            float until = Time.realtimeSinceStartup + 30f;
            while (info == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
                info = FindSave(m.saveSlot);
            }
            if (info == null)
            {
                Core.Log?.Error($"launch: save slot {m.saveSlot} not found - staying in the menu.");
                _running = false;
                yield break;
            }

            Core.Log?.Msg($"launch: loading save {m.saveSlot} ('{info.OrganisationName}').");
            try { LoadManager.Instance.StartGame(info, false, true); }
            catch (Exception e)
            {
                Core.Log?.Error($"launch: StartGame failed ({e.Message}).");
                _running = false;
                yield break;
            }

            if (string.IsNullOrWhiteSpace(m.npcId)) { _running = false; yield break; }

            // Wait for the world and the player, then for the NPC (S1API spawns custom NPCs a few seconds late).
            until = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < until && !(LoadManager.Instance.IsGameLoaded && Player.Local != null))
                yield return new WaitForSeconds(0.5f);
            S1NPC npc = null;
            until = Time.realtimeSinceStartup + 60f;
            while (npc == null && Time.realtimeSinceStartup < until)
            {
                npc = FindNpc(m.npcId);
                if (npc == null) yield return new WaitForSeconds(0.5f);
            }
            _running = false;
            if (npc == null)
            {
                Core.Log?.Error($"launch: NPC '{m.npcId}' is not in the world - player stays at the save position.");
                yield break;
            }

            // S1API activates custom NPCs a few seconds after they register, and until then they sit at the world
            // origin. Wait for a position away from the origin that holds still for a second.
            Vector3 last = npc.transform.position;
            float stableSince = -1f;
            until = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
                if (npc == null) { Core.Log?.Error($"launch: NPC '{m.npcId}' vanished before the teleport."); yield break; }
                Vector3 now = npc.transform.position;
                bool placed = now.sqrMagnitude > 25f;
                if (placed && (now - last).sqrMagnitude < 4f)
                {
                    if (stableSince < 0f) stableSince = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - stableSince >= 1f) break;
                }
                else stableSince = -1f;
                last = now;
            }
            if (npc.transform.position.sqrMagnitude <= 25f)
                Core.Log?.Warning($"launch: NPC '{m.npcId}' is still at the world origin after 45 s - teleporting anyway.");
            TeleportNextTo(npc);
        }

        private static void TeleportNextTo(S1NPC npc)
        {
            try
            {
                var mv = PlayerSingleton<PlayerMovement>.Instance;
                if (mv == null || Player.Local == null) { Core.Log?.Error("launch: no local player to move."); return; }
                Transform t = npc.transform;
                Vector3 pos = t.position + t.forward * 2f + Vector3.up * 0.1f;
                Vector3 look = t.position - pos; look.y = 0f;
                Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                mv.Teleport(pos, rot, false);
                Core.Log?.Msg($"launch: player placed next to '{npc.ID}' at {pos}.");
            }
            catch (Exception e)
            {
                Core.Log?.Error($"launch: teleport failed ({e.Message}).");
            }
        }

        private static SaveInfo FindSave(int slot)
        {
            try
            {
                if (!LoadManager.InstanceExists) return null;
                LoadManager.Instance.RefreshSaveInfo();
                var games = LoadManager.SaveGames;
                if (games == null) return null;
                foreach (var g in games)
                    if (g != null && g.SaveSlotNumber == slot) return g;
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"launch: reading the save list failed ({e.Message}) - retrying.");
            }
            return null;
        }

        private static S1NPC FindNpc(string id)
        {
            foreach (var n in Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry)
                if (n != null && string.Equals(n.ID, id, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        private static void TryDelete()
        {
            try { File.Delete(FilePath); }
            catch (Exception e) { Core.Log?.Warning($"launch marker could not be deleted ({e.Message})."); }
        }
    }
}
