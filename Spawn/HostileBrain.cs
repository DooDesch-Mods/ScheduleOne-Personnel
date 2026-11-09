using System;
using System.Collections.Generic;
using Il2CppFishNet;
using Il2CppScheduleOne.AvatarFramework.Equipping;
using Il2CppScheduleOne.PlayerScripts;
using Personnel.Model;
using UnityEngine;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Spawn
{
    /// <summary>
    /// <c>behavior.weapon</c> and <c>behavior.hostile</c> for Personnel NPCs.
    /// </summary>
    /// <remarks>
    /// The weapon goes to vanilla <c>CombatBehaviour.SetDefaultWeapon</c>, so every fight the NPC has uses it, also
    /// the ones aggression starts. A hostile NPC is checked on the host twice a second: when it is conscious, not
    /// already fighting and its vision cone sees a player (vanilla <c>VisionCone.IsPlayerVisible</c>, 25 m), it
    /// starts a fight with that player through <c>CombatBehaviour.SetTargetAndEnable_Server</c>, the call the
    /// Sewer King uses. Vanilla combat then handles chasing, the weapon and giving up (20 m away or 30 s out of
    /// sight, or after a hit for civilians); the next sighting starts a new fight, at most once every 3 s. Clients do
    /// nothing: the host owns NPC combat.
    /// </remarks>
    internal static class HostileBrain
    {
        /// <summary>Weapon names a pack may use, mapped to vanilla avatar weapon resources. "fists" = no weapon.</summary>
        internal static readonly Dictionary<string, string> Weapons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "fists", "" },
            { "baton", "Avatar/Equippables/Baton" },
            { "brokenbottle", "Avatar/Equippables/BrokenBottle" },
            { "knife", "Avatar/Equippables/Knife" },
            { "m1911", "Avatar/Equippables/M1911" },
            { "m1911_gold", "Avatar/Equippables/M1911_Gold" },
            { "pumpshotgun", "Avatar/Equippables/PumpShotgun" },
            { "revolver", "Avatar/Equippables/Revolver" },
            { "taser", "Avatar/Equippables/Taser" },
        };

        private const float CheckIntervalSeconds = 0.5f;

        // A fight that vanilla ends right after it starts (no path to the player) would otherwise restart every check.
        private const float ReengageDelaySeconds = 3f;

        private sealed class Entry
        {
            public S1NPC Npc;
            public string Id;
            public int Engagements;
            public float NextEngageAt;
            public string LastWait;
        }

        private static readonly List<Entry> Hostiles = new List<Entry>();
        private static float _nextCheckAt;

        /// <summary>Sets the combat weapon and, for a hostile definition, starts watching the NPC.</summary>
        internal static void Apply(GameObject go, NpcDef def)
        {
            if (def?.Behavior == null) return;
            S1NPC npc = go != null ? go.GetComponent<S1NPC>() : null;
            if (npc == null)
            {
                if (def.Behavior.Hostile || !string.IsNullOrWhiteSpace(def.Behavior.Weapon))
                    Core.Log?.Error($"'{def.Id}': no game NPC on the object - weapon and hostile not applied.");
                return;
            }

            ApplyWeapon(npc, def);

            if (!def.Behavior.Hostile) return;
            Hostiles.RemoveAll(e => e.Npc == null || e.Npc.WasCollected || e.Npc == npc);
            Hostiles.Add(new Entry { Npc = npc, Id = def.Id });
            Core.Log?.Msg($"'{def.Id}': hostile - attacks players it sees{(InstanceFinder.IsServer ? "" : " (decided by the host)")}.");
        }

        private static void ApplyWeapon(S1NPC npc, NpcDef def)
        {
            string raw = def.Behavior.Weapon;
            if (string.IsNullOrWhiteSpace(raw)) return;

            string path;
            if (!Weapons.TryGetValue(raw, out path))
            {
                if (raw.IndexOf('/') < 0)
                {
                    Core.Log?.Warning($"'{def.Id}': unknown behavior.weapon '{raw}' - fights with fists. Known: {string.Join(", ", Weapons.Keys)}.");
                    return;
                }
                path = raw;
            }
            if (path.Length == 0) return;

            AvatarWeapon weapon = null;
            try
            {
                GameObject prefab = Resources.Load<GameObject>(path);
                if (prefab != null) weapon = prefab.GetComponent<AvatarWeapon>();
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"'{def.Id}': loading weapon '{path}' failed ({ex.Message}) - fights with fists.");
                return;
            }
            if (weapon == null)
            {
                Core.Log?.Error($"'{def.Id}': '{path}' is not a vanilla weapon - fights with fists.");
                return;
            }

            try { npc.Behaviour.CombatBehaviour.SetDefaultWeapon(weapon); }
            catch (Exception ex)
            {
                Core.Log?.Error($"'{def.Id}': setting weapon '{path}' failed ({ex.Message}) - fights with fists.");
                return;
            }
            Core.Log?.Msg($"'{def.Id}': combat weapon {path}.");
        }

        /// <summary>Called every frame from <see cref="Core.OnUpdate"/>; does work twice a second on the host.</summary>
        internal static void Tick()
        {
            if (Hostiles.Count == 0) return;
            float now = Time.time;
            if (now < _nextCheckAt) return;
            _nextCheckAt = now + CheckIntervalSeconds;
            if (!InstanceFinder.IsServer) return;

            for (int i = Hostiles.Count - 1; i >= 0; i--)
            {
                Entry e = Hostiles[i];
                if (e.Npc == null || e.Npc.WasCollected)
                {
                    Core.Log?.Msg($"'{e.Id}': hostile NPC is gone - no longer watched.");
                    Hostiles.RemoveAt(i);
                    continue;
                }
                try { Check(e); }
                catch (Exception ex)
                {
                    Core.Log?.Error($"'{e.Id}': hostile check failed ({ex.Message}) - no longer watched.");
                    Hostiles.RemoveAt(i);
                }
            }
        }

        private static void Check(Entry e)
        {
            S1NPC npc = e.Npc;
            if (!npc.IsConscious) { Wait(e, "NPC is unconscious"); return; }
            if (npc.Health == null || npc.Health.IsDead) { Wait(e, "NPC is dead"); return; }
            var combat = npc.Behaviour != null ? npc.Behaviour.CombatBehaviour : null;
            if (combat == null) { Wait(e, "NPC has no combat behaviour"); return; }
            if (combat.Enabled) { Wait(e, "fighting"); return; }
            if (Time.time < e.NextEngageAt) { Wait(e, "waiting before the next fight"); return; }
            var cone = npc.Awareness != null ? npc.Awareness.VisionCone : null;
            if (cone == null) { Wait(e, "NPC has no vision cone"); return; }

            var players = Player.PlayerList;
            int targetable = 0;
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null || p.IsArrested || p.IsUnconscious || p.Health == null || !p.Health.IsAlive) continue;
                targetable++;
                if (!cone.IsPlayerVisible(p)) continue;
                combat.SetTargetAndEnable_Server(p.NetworkObject);
                e.Engagements++;
                e.NextEngageAt = Time.time + ReengageDelaySeconds;
                e.LastWait = null;
                Core.Log?.Msg($"'{e.Id}': hostile - attacking {p.PlayerName} (#{e.Engagements}).");
                return;
            }
            Wait(e, targetable == 0 ? "every player is arrested, unconscious or dead" : "no player in sight");
        }

        // Logs why a hostile NPC is not attacking, once per change of reason, so a quiet NPC is explainable from the log.
        private static void Wait(Entry e, string reason)
        {
            if (e.LastWait == reason) return;
            e.LastWait = reason;
            Core.Log?.Msg($"'{e.Id}': hostile - not attacking: {reason}.");
        }

        internal static int Count => Hostiles.Count;
    }
}
