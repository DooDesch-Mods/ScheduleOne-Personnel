using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DooDesch.AvatarKit;
using Il2CppFishNet;
using Il2CppFishNet.Managing.Object;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.AvatarFramework;
using Il2CppScheduleOne.Core.Avatar;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Framework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using MelonLoader;
using Personnel.Model;
using Personnel.Registration;
using UnityEngine;
using UnityEngine.SceneManagement;
using LawBelt = Il2CppScheduleOne.Law.PoliceBelt;
using MapSingleton = Il2CppScheduleOne.Map.Map;
using Object = UnityEngine.Object;

namespace Personnel.Police
{
    /// <summary>
    /// Turns every definition with a <c>police</c> block into one more officer of the local police station.
    /// </summary>
    /// <remarks>
    /// S1API cannot do this: its prefab pipeline clones a civilian, and every police behaviour hard-casts its NPC to
    /// PoliceOfficer. The game has no officer prefab either, all ten stand in Main as scene objects. So a scene
    /// officer is cloned into a dormant template and registered as a spawnable prefab.
    ///
    /// The templates live in a FishNet collection of their own. Spawn messages carry the collection id and the
    /// index inside it, so host and client agree as long as both add the same definitions in the same order, no
    /// matter how far S1API has got with the default collection.
    ///
    /// The templates are rebuilt on every load of Main. They hold references into the scene that dies with it, and
    /// the sceneLoaded callback runs before FishNet tells the server this client finished loading, so a spawn from
    /// the host always finds them.
    ///
    /// Officers are not saved (PoliceOfficer.ShouldSave is false), so a card that is removed leaves nothing behind.
    /// </remarks>
    internal static class PoliceRoster
    {
        private const ushort CollectionId = 4711;
        private const string BeltPath = "Avatar/Accessories/Waist/PoliceBelt/PoliceBelt";
        private const float SpawnWaitSeconds = 180f;

        private static readonly List<GameObject> Templates = new List<GameObject>();
        private static readonly Dictionary<string, NpcDef> DefsByNpcId = new Dictionary<string, NpcDef>(StringComparer.Ordinal);
        private static readonly HashSet<int> Dressed = new HashSet<int>();
        private static readonly HashSet<int> BeltMissingLogged = new HashSet<int>();
        private static readonly string EmptyGuid = Guid.Empty.ToString();
        private static int _generation;

        internal static int TemplateCount => Templates.Count;

        /// <summary>Called from the sceneLoaded callback, before any spawn message for this scene can arrive.</summary>
        internal static void OnSceneLoaded(string sceneName)
        {
            if (sceneName != "Main") return;
            _generation++;
            Templates.Clear();
            DefsByNpcId.Clear();
            Dressed.Clear();
            BeltMissingLogged.Clear();

            var defs = new List<NpcDef>();
            foreach (NpcDef def in NpcRegistry.AllDefs)
                if (def?.Police != null) defs.Add(def);
            if (defs.Count == 0)
            {
                Core.Log?.Msg("Police: no definition with a police block, no extra officers.");
                return;
            }
            defs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            try
            {
                BuildTemplates(defs);
            }
            catch (Exception e)
            {
                Core.Log?.Error("Police: building the officer templates failed, no extra officers this load: " + e);
                Templates.Clear();
                DefsByNpcId.Clear();
            }
        }

        /// <summary>Called once the scene is up: spawns the officers on the host and dresses them on every peer.</summary>
        internal static void OnSceneInitialized(string sceneName)
        {
            if (sceneName != "Main" || Templates.Count == 0) return;
            MelonCoroutines.Start(SpawnWhenReady(_generation));
            MelonCoroutines.Start(DressLoop(_generation));
        }

        private static void BuildTemplates(List<NpcDef> defs)
        {
            NetworkObject sourceNob = FindSourceOfficer(out PoliceOfficer source);
            if (sourceNob == null)
            {
                Core.Log?.Error("Police: no officer found in the scene to copy, no extra officers this load.");
                return;
            }

            var networkManager = InstanceFinder.NetworkManager;
            if (networkManager == null)
            {
                Core.Log?.Error("Police: no NetworkManager at scene load, no extra officers this load.");
                return;
            }

            var collection = networkManager.GetPrefabObjects<SinglePrefabObjects>(CollectionId, true);
            if (collection == null)
            {
                Core.Log?.Error("Police: FishNet returned no prefab collection, no extra officers this load.");
                return;
            }
            collection.Clear();

            // Cloned under an inactive parent, so the copies run neither Awake nor Start.
            var holder = new GameObject("PersonnelPoliceTemplates");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, source.gameObject.scene);

            foreach (NpcDef def in defs)
            {
                GameObject template = BuildTemplate(source, holder.transform, def);
                if (template == null) continue;
                collection.AddObject(template.GetComponent<NetworkObject>(), false);
                Templates.Add(template);
            }
            collection.InitializePrefabRange(0);

            foreach (GameObject template in Templates)
            {
                var nob = template.GetComponent<NetworkObject>();
                Core.Log?.Msg($"Police: template '{template.name}' registered as prefab {nob.PrefabId} in collection {nob.SpawnableCollectionId}.");
            }
            Core.Log?.Msg($"Police: copied '{source.name}'; it still reads as '{source.FirstName}' with id '{source.ID}'.");
        }

        private static NetworkObject FindSourceOfficer(out PoliceOfficer source)
        {
            source = null;
            NetworkObject found = null;
            // The same officer on every peer, though nothing depends on it: the copy takes all its identity from the definition.
            foreach (PoliceOfficer officer in Object.FindObjectsOfType<PoliceOfficer>(true))
            {
                if (officer == null) continue;
                var nob = officer.GetComponent<NetworkObject>();
                if (nob == null) continue;
                if (source == null || string.CompareOrdinal(officer.name, source.name) < 0)
                {
                    source = officer;
                    found = nob;
                }
            }
            return found;
        }

        private static GameObject BuildTemplate(PoliceOfficer source, Transform holder, NpcDef def)
        {
            GameObject clone = Object.Instantiate(source.gameObject, holder);
            clone.name = "PersonnelPolice_" + def.Id;
            clone.SetActive(false);

            var officer = clone.GetComponent<PoliceOfficer>();
            var nob = clone.GetComponent<NetworkObject>();

            // A scene id makes FishNet resolve the spawn to the copied scene officer instead of this prefab.
            nob._SceneId_k__BackingField = 0UL;

            // What the source already wears came along as children the copy's avatar does not know about: the avatar
            // objects, and their attachments on the bones, whose Start throws without a parent object.
            int orphans = 0;
            foreach (AvatarObject avatarObject in clone.GetComponentsInChildren<AvatarObject>(true))
            {
                Object.Destroy(avatarObject.gameObject);
                orphans++;
            }
            foreach (AvatarAttachment attachment in clone.GetComponentsInChildren<AvatarAttachment>(true))
            {
                Object.Destroy(attachment.gameObject);
                orphans++;
            }
            foreach (SkinnedAvatarAttachment attachment in clone.GetComponentsInChildren<SkinnedAvatarAttachment>(true))
            {
                Object.Destroy(attachment.gameObject);
                orphans++;
            }

            // The copy of the data object means the identity is right from Awake on, on host and client alike.
            string npcId = def.SaveId ?? def.Id;
            var data = Object.Instantiate(officer._defaultNPCData);
            BasicInfo info = data.GetOriginalData().BasicInfo;
            SplitName(def.DisplayName, out string first, out string last);
            info.ID = npcId;
            info.FirstName = first;
            info.HasLastName = last.Length > 0;
            info.LastName = last;
            officer._defaultNPCData = data;

            officer.BakedGUID = GuidFor(npcId).ToString();
            DefsByNpcId[npcId] = def;

            Core.Log?.Msg($"Police: template for '{def.Id}' as '{first} {last}', {orphans} avatar object(s) of the source removed.");
            return clone;
        }

        private static IEnumerator SpawnWhenReady(int generation)
        {
            if (!InstanceFinder.IsServer)
            {
                Core.Log?.Msg("Police: this peer is a client, the host spawns the officers.");
                yield break;
            }

            float waited = 0f;
            while (!WorldReady())
            {
                if (generation != _generation)
                {
                    Core.Log?.Msg("Police: the scene changed before the world was ready, spawn cancelled.");
                    yield break;
                }
                if (waited > SpawnWaitSeconds)
                {
                    Core.Log?.Error($"Police: the world was not ready after {SpawnWaitSeconds} s, no extra officers this load.");
                    yield break;
                }
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            var spawned = new List<PoliceOfficer>();
            foreach (GameObject template in Templates)
            {
                PoliceOfficer officer = SpawnOfficer(template);
                if (officer != null) spawned.Add(officer);
            }

            // NPC.Start has to run before an officer can walk into the station.
            yield return null;
            yield return null;

            foreach (PoliceOfficer officer in spawned)
            {
                if (generation != _generation || officer == null) yield break;
                try
                {
                    officer.Deactivate();
                    Core.Log?.Msg($"Police: {officer.ID} went into the police station.");
                }
                catch (Exception e)
                {
                    Core.Log?.Error($"Police: {officer.ID} could not enter the police station: {e}");
                }
            }
        }

        private static bool WorldReady()
        {
            var load = Singleton<LoadManager>.Instance;
            return load != null && load.IsGameLoaded && !load.IsLoading
                && Player.Local != null
                && Singleton<MapSingleton>.Instance != null
                && Singleton<MapSingleton>.Instance.PoliceStation != null;
        }

        private static PoliceOfficer SpawnOfficer(GameObject template)
        {
            try
            {
                PoliceStation station = Singleton<MapSingleton>.Instance.PoliceStation;
                Transform at = station.SpawnPoint;
                GameObject instance = Object.Instantiate(template, at.position, at.rotation);
                instance.name = template.name;
                instance.SetActive(true);
                InstanceFinder.ServerManager.Spawn(instance.GetComponent<NetworkObject>(), null, default(Scene));
                var officer = instance.GetComponent<PoliceOfficer>();
                Core.Log?.Msg($"Police: spawned {officer.ID} ('{officer.FirstName} {officer.LastName}') with object id {instance.GetComponent<NetworkObject>().ObjectId}.");
                return officer;
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Police: spawning '{template.name}' failed: {e}");
                return null;
            }
        }

        /// <summary>Applies the look to every officer of ours as it appears, host and client alike, late joiners included.</summary>
        private static IEnumerator DressLoop(int generation)
        {
            var wait = new WaitForSeconds(1f);
            while (generation == _generation)
            {
                DressNew();
                yield return wait;
            }
        }

        private static void DressNew()
        {
            var officers = PoliceOfficer.Officers;
            if (officers == null) return;
            for (int i = 0; i < officers.Count; i++)
            {
                PoliceOfficer officer = officers[i];
                if (officer == null || !officer.HasNPCData || officer.Avatar == null) continue;
                if (!DefsByNpcId.TryGetValue(officer.ID, out NpcDef def)) continue;
                // PoliceOfficer.Start reads the belt off the avatar and would overwrite what Dress sets. NPC.Start sets the GUID.
                if (officer.GUID.ToString() == EmptyGuid) continue;
                if (Dressed.Add(officer.GetInstanceID())) Dress(officer, def);
                else if (officer.PoliceBelt == null) RefindBelt(officer);
            }
        }

        // The officer's belt reference can go null after dressing, and a pursuit throws without it.
        private static void RefindBelt(PoliceOfficer officer)
        {
            LawBelt belt = officer.Avatar.GetComponentInChildren<LawBelt>(true);
            officer._PoliceBelt_k__BackingField = belt;
            if (belt != null) Core.Log?.Msg($"Police: {officer.ID} lost its belt reference, set it again.");
            else if (BeltMissingLogged.Add(officer.GetInstanceID()))
                Core.Log?.Error($"Police: {officer.ID} lost its police belt, the game will throw when it chases someone.");
        }

        private static void Dress(PoliceOfficer officer, NpcDef def)
        {
            try
            {
                AvatarSettings settings = API.BuildAvatarSettings(def);
                if (settings == null)
                {
                    Core.Log?.Error($"Police: no appearance could be built for {officer.ID}, it keeps the copied officer's look.");
                    return;
                }
                bool added = EnsureBelt(settings);
                AvatarLayerSlots.LoadAndClean(officer.Avatar, settings);
                API.ApplyDistortion(officer.Avatar, def);

                // The game draws the baton, taser and gun from this belt during a pursuit and throws when it is missing.
                LawBelt belt = officer.Avatar.GetComponentInChildren<LawBelt>(true);
                officer._PoliceBelt_k__BackingField = belt;
                if (belt == null)
                    Core.Log?.Error($"Police: {officer.ID} has no police belt after dressing, the game will throw when it chases someone.");
                else
                    Core.Log?.Msg($"Police: dressed {officer.ID} with belt {(added ? "added" : "from the card")}, belt active={belt.gameObject.activeInHierarchy}.");
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Police: dressing {officer.ID} failed: {e}");
            }
        }

        private static bool EnsureBelt(AvatarSettings settings)
        {
            var accessories = settings.AccessorySettings;
            for (int i = 0; i < accessories.Count; i++)
                if (string.Equals(accessories[i].path, BeltPath, StringComparison.OrdinalIgnoreCase))
                    return false;
            var belt = new AvatarSettings.AccessorySetting();
            belt.path = BeltPath;
            belt.color = Color.white;
            accessories.Add(belt);
            return true;
        }

        private static void SplitName(string displayName, out string first, out string last)
        {
            string name = string.IsNullOrWhiteSpace(displayName) ? "Officer" : displayName.Trim();
            int space = name.IndexOf(' ');
            first = space < 0 ? name : name.Substring(0, space);
            last = space < 0 ? string.Empty : name.Substring(space + 1).Trim();
        }

        private static Guid GuidFor(string npcId)
        {
            using var md5 = MD5.Create();
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("personnel.police." + npcId)));
        }

#if DEBUG
        /// <summary>Lists our officers: what the pool, the belt and the current activity say about each.</summary>
        internal static void Report(Action<string> log)
        {
            log($"police: {Templates.Count} template(s), {PoliceOfficer.Officers?.Count ?? 0} officer(s) in the game.");
            PoliceStation station = Singleton<MapSingleton>.Instance?.PoliceStation;
            log($"police: {station?.OfficerPool?.Count ?? 0} officer(s) in the station pool.");
            var officers = PoliceOfficer.Officers;
            if (officers == null) return;
            for (int i = 0; i < officers.Count; i++)
            {
                PoliceOfficer officer = officers[i];
                if (officer == null || !DefsByNpcId.ContainsKey(officer.ID)) continue;
                bool pooled = station != null && station.OfficerPool.Contains(officer);
                string behaviour = officer.Behaviour?.activeBehaviour?.Name ?? "none";
                log($"police: {officer.ID} '{officer.FirstName} {officer.LastName}' pooled={pooled} building={officer.isInBuilding} " +
                    $"belt={officer.PoliceBelt != null} behaviour={behaviour} pos={officer.transform.position} " +
                    $"guid={officer.GUID} objectId={officer.ObjectId}");
            }
        }

        /// <summary>Pulls every officer of the station out onto the local player, the way a dispatch does.</summary>
        internal static void Dispatch(Action<string> log)
        {
            PoliceStation station = Singleton<MapSingleton>.Instance?.PoliceStation;
            if (station == null || Player.Local == null)
            {
                log("police: no station or no local player.");
                return;
            }
            int count = station.OfficerPool.Count;
            station.Dispatch(count, Player.Local, PoliceStation.EDispatchType.Auto, false);
            log($"police: dispatched {count} officer(s).");
        }

        /// <summary>Starts a foot pursuit of the local player for every officer of ours, wherever it is.</summary>
        internal static void Pursue(Action<string> log)
        {
            var officers = PoliceOfficer.Officers;
            if (officers == null || Player.Local == null)
            {
                log("police: no officers or no local player.");
                return;
            }
            for (int i = 0; i < officers.Count; i++)
            {
                PoliceOfficer officer = officers[i];
                if (officer == null || !DefsByNpcId.ContainsKey(officer.ID)) continue;
                officer.BeginFootPursuit_Networked(Player.Local.PlayerCode, false);
                log($"police: {officer.ID} pursues {Player.Local.PlayerCode}.");
            }
        }
#endif
    }
}
