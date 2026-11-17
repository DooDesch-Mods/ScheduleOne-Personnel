using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppSteamworks;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using CanvasScaler = UnityEngine.UI.CanvasScaler;

namespace Personnel.Content
{
    /// <summary>
    /// Compares the loaded NPC packs of a co-op client with the host's. S1API registers every pack NPC as a FishNet
    /// spawnable prefab, so peers with different packs disagree on prefab ids: NPCs and the items they hold arrive
    /// broken on the client (handlers without a user, errors every frame). Joining stays allowed; the client gets an
    /// error log line and an in-game notice that names the packs.
    /// <para>
    /// The host writes its list to the Steam lobby under <see cref="LobbyKey"/>; the client reads it once per lobby.
    /// Format: <c>1|name*fingerprint|name*fingerprint</c>, pack folder names in load order. The fingerprint hashes the
    /// pack's NPC ids, so a pack with the same folder name but other NPCs counts as different. A list longer than
    /// <see cref="MaxListLength"/> is written as <c>1#count#fingerprint</c> (lobby data is one 8 KB blob shared by
    /// every key), and the client can then only say that the packs differ.
    /// </para>
    /// </summary>
    internal static class PackSync
    {
        internal const string LobbyKey = "personnel_packs";
        private const int MaxListLength = 2048;
        private const float PollSeconds = 2f;
        private const float HostListWaitSeconds = 60f;
        private const float NoticeSeconds = 15f;
        private const int NoticeNamesShown = 3;
        private const float NoticeDelaySeconds = 2f;
        private static float _visibleSince = -1f;
        private const int NoticeSortingOrder = 50;
        // Left inset of the notice texts inside the hidden icon's space.
        private const float NoticeTextLeft = 10f;
        private static GameObject _noticeRoot;
        private static float _noticeUntil;

        private sealed class Pack
        {
            public string Name;
            public string Fingerprint;
        }

        private static readonly List<Pack> Loaded = new List<Pack>();

        private static float _nextPoll;
        private static ulong _publishedLobby;
        private static ulong _checkedLobby;
        private static ulong _waitingLobby;
        private static float _waitingSince;
        private static string _lastState;

        private static string _pendingTitle;
        private static string _pendingText;
        private static string _lastTitle;
        private static string _lastText;

        /// <summary>Outcome of the last comparison, for <c>personnel packs</c>.</summary>
        internal static string LastResult { get; private set; } = "not compared yet";

        internal static void Clear()
        {
            Loaded.Clear();
            _publishedLobby = 0;
            _checkedLobby = 0;
        }

        internal static void Record(string packName, IList<string> npcIds)
        {
            if (npcIds == null || npcIds.Count == 0) return;
            Loaded.Add(new Pack { Name = packName, Fingerprint = Fingerprint(npcIds) });
        }

        internal static string Serialize()
        {
            var sb = new StringBuilder("1");
            foreach (Pack p in Loaded) sb.Append('|').Append(p.Name).Append('*').Append(p.Fingerprint);
            if (sb.Length <= MaxListLength) return sb.ToString();
            string all = Fingerprint(Loaded.Select(p => p.Name + "*" + p.Fingerprint).ToList());
            return $"1#{Loaded.Count}#{all}";
        }

        internal static void Tick()
        {
            HideExpiredNotice();
            ShowPendingNotice();
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;

            Lobby lobby;
            ulong lobbyId;
            bool isHost;
            try
            {
                lobby = PersistentSingleton<Lobby>.Instance;
                if (lobby == null) { State("info", "no lobby object yet; packs not compared."); return; }
                if (!lobby.IsInLobby)
                {
                    State("info", "not in a lobby; packs not compared.");
                    _publishedLobby = 0; _checkedLobby = 0; _waitingLobby = 0;
                    return;
                }
                // Lobby.LobbyID is never assigned by the game; the Steam id lives in SteamLobbyService._lobbyID.
                var steam = lobby._lobbyService?.TryCast<SteamLobbyService>();
                if (steam == null) { State("info", "the game runs without a Steam lobby service; packs not compared."); return; }
                lobbyId = steam._lobbyID;
                isHost = lobby.IsHost;
            }
            catch (Exception e) { State("error", $"lobby state could not be read: {e.Message}"); return; }
            if (lobbyId == 0) { State("info", "in a lobby whose id is not resolved yet; packs not compared."); return; }

            if (isHost) Publish(lobbyId);
            else Compare(lobbyId);
        }

        private static void Publish(ulong lobbyId)
        {
            if (_publishedLobby == lobbyId) return;
            string value = Serialize();
            try
            {
                var sid = new CSteamID(lobbyId);
                if (!SteamMatchmaking.SetLobbyData(sid, LobbyKey, value))
                {
                    State("error", $"Steam refused the pack list for lobby {lobbyId}; clients cannot compare their packs.");
                    return;
                }
                string back = SteamMatchmaking.GetLobbyData(sid, LobbyKey);
                if (back != value)
                {
                    State("error", $"the pack list on lobby {lobbyId} reads back differently; clients cannot compare their packs.");
                    return;
                }
            }
            catch (Exception e) { State("error", $"the pack list could not be written to lobby {lobbyId}: {e.Message}"); return; }
            _publishedLobby = lobbyId;
            _lastState = null;
            LastResult = $"host: published {Loaded.Count} pack(s)";
            Core.Log?.Msg($"[packs] published {Loaded.Count} NPC pack(s) to lobby {lobbyId} for joining clients: {Names(Loaded.Select(p => p.Name))}.");
        }

        private static void Compare(ulong lobbyId)
        {
            if (_checkedLobby == lobbyId) return;
            string hostValue;
            try { hostValue = SteamMatchmaking.GetLobbyData(new CSteamID(lobbyId), LobbyKey); }
            catch (Exception e) { State("error", $"the host's pack list could not be read from lobby {lobbyId}: {e.Message}"); return; }

            if (string.IsNullOrEmpty(hostValue))
            {
                if (_waitingLobby != lobbyId) { _waitingLobby = lobbyId; _waitingSince = Time.unscaledTime; }
                if (Time.unscaledTime - _waitingSince < HostListWaitSeconds)
                {
                    State("info", $"waiting for the host's pack list on lobby {lobbyId}.");
                    return;
                }
                _checkedLobby = lobbyId;
                LastResult = "client: the host published no pack list";
                Core.Log?.Warning($"[packs] the host published no NPC pack list on lobby {lobbyId} within {HostListWaitSeconds:0} s " +
                                  "(host without Personnel, or an older Personnel); packs not compared.");
                return;
            }

            _checkedLobby = lobbyId;
            _lastState = null;
            string mine = Serialize();
            if (hostValue == mine)
            {
                LastResult = $"client: match ({Loaded.Count} pack(s))";
                Core.Log?.Msg($"[packs] NPC packs match the host's ({Loaded.Count} pack(s)).");
                return;
            }

            if (!TryParse(hostValue, out List<Pack> hostPacks))
            {
                LastResult = "client: differ (host list too long to name packs)";
                Core.Log?.Error($"[packs] NPC packs differ from the host's (host list: {hostValue}, ours: {Loaded.Count} pack(s)). " +
                                "NPCs and the items they hold desync until both sides load the same packs.");
                Queue("NPC packs differ from the host", "Install the same Personnel packs as the host.");
                return;
            }

            var hostByName = hostPacks.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Fingerprint, StringComparer.OrdinalIgnoreCase);
            var mineByName = Loaded.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Fingerprint, StringComparer.OrdinalIgnoreCase);
            var missing = hostPacks.Where(p => !mineByName.ContainsKey(p.Name)).Select(p => p.Name).ToList();
            var extra = Loaded.Where(p => !hostByName.ContainsKey(p.Name)).Select(p => p.Name).ToList();
            var changed = Loaded.Where(p => hostByName.TryGetValue(p.Name, out string fp) && fp != p.Fingerprint)
                .Select(p => p.Name).ToList();

            if (missing.Count == 0 && extra.Count == 0 && changed.Count == 0)
            {
                // Same packs and contents in another order: the folder sort differs between the two machines.
                LastResult = "client: same packs, other load order";
                Core.Log?.Error($"[packs] NPC packs load in another order than on the host (host: {Names(hostPacks.Select(p => p.Name))}; " +
                                $"ours: {Names(Loaded.Select(p => p.Name))}). NPCs and the items they hold desync.");
                Queue("NPC packs load in another order than on the host", "Rename the pack folders the same as the host's.");
                return;
            }

            LastResult = $"client: missing [{string.Join(", ", missing)}] extra [{string.Join(", ", extra)}] changed [{string.Join(", ", changed)}]";
            Core.Log?.Error($"[packs] NPC packs differ from the host's: missing {Names(missing)}; extra {Names(extra)}; " +
                            $"different content {Names(changed)}. NPCs and the items they hold desync until both sides load the same packs.");

            var parts = new List<string>();
            if (missing.Count > 0) parts.Add("Missing: " + Short(missing));
            if (extra.Count > 0) parts.Add("Extra: " + Short(extra));
            if (changed.Count > 0) parts.Add("Different: " + Short(changed));
            Queue("NPC packs differ from the host", string.Join(". ", parts) + ".");
        }

        private static bool TryParse(string value, out List<Pack> packs)
        {
            packs = new List<Pack>();
            if (!value.StartsWith("1|", StringComparison.Ordinal) && value != "1") return false;
            foreach (string entry in value.Split('|').Skip(1))
            {
                int star = entry.LastIndexOf('*');
                if (star <= 0) continue;
                packs.Add(new Pack { Name = entry.Substring(0, star), Fingerprint = entry.Substring(star + 1) });
            }
            return true;
        }

        private static void Queue(string title, string text)
        {
            _pendingTitle = _lastTitle = title;
            _pendingText = _lastText = text;
            Core.Log?.Msg($"[packs] notice queued until the world is loaded: \"{title}\" / \"{text}\".");
        }

        /// <summary>Shows the queued notice once the local player stands in a loaded world.</summary>
        private static void ShowPendingNotice()
        {
            if (_pendingTitle == null) return;
            try
            {
                if (Player.Local == null) { _visibleSince = -1f; return; }
                var load = PersistentSingleton<LoadManager>.Instance;
                var screen = PersistentSingleton<LoadingScreen>.Instance;
                var black = Singleton<BlackOverlay>.Instance;
                // S1API keeps the loading screen open after the game reports loaded, and the world fades in from
                // black; a notice before that is gone before the player sees anything.
                // A player new to the host's save first sits through the intro cutscene and the character creator.
                var creator = Singleton<Il2CppScheduleOne.CharacterCreator.CharacterCreator>.Instance;
                bool fading = black != null && (black.isShown || (black.group != null && black.group.alpha > 0.05f));
                if ((load != null && load.IsLoading) || (screen != null && screen.IsOpen) || fading
                    || !Player.Local._hasCompletedIntro || (creator != null && creator.IsOpen))
                {
                    _visibleSince = -1f;
                    return;
                }
                if (_visibleSince < 0f) _visibleSince = Time.unscaledTime;
                if (Time.unscaledTime - _visibleSince < NoticeDelaySeconds) return;
                if (!ShowNotice(_pendingTitle, _pendingText)) return;
                Core.Log?.Msg($"[packs] notice shown: \"{_pendingTitle}\" / \"{_pendingText}\".");
            }
            catch (Exception e) { Core.Log?.Error($"[packs] notice \"{_pendingTitle}\" could not be shown: {e.Message}"); }
            _pendingTitle = null;
            _pendingText = null;
        }

        /// <summary>
        /// Draws the vanilla notification on an overlay canvas of its own. The game's notification list cannot carry
        /// it: the HUD sits under the scene network object "UI", which a client activates only when the host's spawn
        /// message for it arrives, and with different packs that message never applies, so the client has no HUD at
        /// all. Layout and scaling are copied from the vanilla list and HUD canvas, which keep their serialized values
        /// while inactive. Returns false while the notification prefab is not available yet.
        /// </summary>
        private static bool ShowNotice(string title, string text)
        {
            var notifications = Singleton<NotificationsManager>.Instance;
            if (notifications == null || notifications.NotificationPrefab == null || notifications.EntryContainer == null)
            {
                State("info", "the notification prefab is not loaded yet; the pack notice waits.");
                return false;
            }
            HideNotice("replaced by a new notice");

            var root = new GameObject("Personnel_PackNotice");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = NoticeSortingOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            var hudScaler = Singleton<HUD>.Instance?.canvas?.GetComponent<CanvasScaler>();
            if (hudScaler != null)
            {
                scaler.uiScaleMode = hudScaler.uiScaleMode;
                scaler.referenceResolution = hudScaler.referenceResolution;
                scaler.screenMatchMode = hudScaler.screenMatchMode;
                scaler.matchWidthOrHeight = hudScaler.matchWidthOrHeight;
            }
            else Core.Log?.Warning("[packs] no HUD canvas scaler to copy; the notice uses the default scaling.");

            var source = notifications.EntryContainer;
            var listObject = new GameObject("Entries");
            var list = listObject.AddComponent<RectTransform>();
            list.SetParent(root.transform, false);
            list.anchorMin = source.anchorMin;
            list.anchorMax = source.anchorMax;
            list.pivot = source.pivot;
            list.anchoredPosition = source.anchoredPosition;
            list.sizeDelta = source.sizeDelta;
            var sourceLayout = source.GetComponent<VerticalLayoutGroup>();
            if (sourceLayout != null)
            {
                var layout = listObject.AddComponent<VerticalLayoutGroup>();
                var p = sourceLayout.padding;
                layout.padding = new RectOffset(p.left, p.right, p.top, p.bottom);
                layout.spacing = sourceLayout.spacing;
                layout.childAlignment = sourceLayout.childAlignment;
                layout.childControlWidth = sourceLayout.childControlWidth;
                layout.childControlHeight = sourceLayout.childControlHeight;
                layout.childForceExpandWidth = sourceLayout.childForceExpandWidth;
                layout.childForceExpandHeight = sourceLayout.childForceExpandHeight;
            }
            else Core.Log?.Warning("[packs] the vanilla notification list has no layout group; the notice keeps its prefab size.");
            var sourceFitter = source.GetComponent<ContentSizeFitter>();
            if (sourceFitter != null)
            {
                var fitter = listObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = sourceFitter.horizontalFit;
                fitter.verticalFit = sourceFitter.verticalFit;
            }

            var entry = UnityEngine.Object.Instantiate(notifications.NotificationPrefab, list);
            var container = entry.transform.Find("Container")?.TryCast<RectTransform>();
            if (container == null)
            {
                UnityEngine.Object.Destroy(root);
                Core.Log?.Error("[packs] the notification prefab has no Container; the pack notice is not shown.");
                return true;
            }
            container.anchoredPosition = new Vector2(0f, container.anchoredPosition.y);
            // Vanilla passes an app icon; the pack notice has none, and an empty image would draw a white square.
            // The texts take over the space the icon leaves.
            var icon = container.Find("AppIcon");
            if (icon != null) icon.gameObject.SetActive(false);
            var titleText = container.Find("Title")?.GetComponent<TextMeshProUGUI>();
            var subtitleText = container.Find("Subtitle")?.GetComponent<TextMeshProUGUI>();
            if (titleText != null)
            {
                titleText.text = title;
                titleText.rectTransform.offsetMin = new Vector2(NoticeTextLeft, titleText.rectTransform.offsetMin.y);
            }
            else Core.Log?.Warning("[packs] the notification prefab has no Title text.");
            if (subtitleText != null)
            {
                subtitleText.text = text;
                subtitleText.rectTransform.offsetMin = new Vector2(NoticeTextLeft, subtitleText.rectTransform.offsetMin.y);
                FitSubtitle(entry, list, subtitleText);
            }
            else Core.Log?.Warning("[packs] the notification prefab has no Subtitle text.");

            _noticeRoot = root;
            _noticeUntil = Time.unscaledTime + NoticeSeconds;
            return true;
        }

        /// <summary>
        /// The vanilla subtitle is one 25 px line that ends in an ellipsis, too short for a list of pack names, so the
        /// pack notice wraps it and grows the entry by the extra lines.
        /// </summary>
        private static void FitSubtitle(GameObject entry, RectTransform list, TextMeshProUGUI subtitle)
        {
            subtitle.enableWordWrapping = true;
            subtitle.overflowMode = TextOverflowModes.Overflow;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            var rect = subtitle.rectTransform;
            var width = rect.rect.width;
            if (width <= 0f)
            {
                Core.Log?.Warning($"[packs] the notice subtitle has no width yet ({width}); it keeps the prefab height.");
                return;
            }
            var extra = subtitle.GetPreferredValues(subtitle.text, width, 0f).y - rect.rect.height;
            if (extra <= 0f)
            {
                Core.Log?.Msg($"[packs] the notice subtitle fits one line ({width:0} wide).");
                return;
            }
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y + extra);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y + extra / 2f);
            var element = entry.GetComponent<LayoutElement>();
            if (element != null) element.preferredHeight += extra;
            else Core.Log?.Warning("[packs] the notification prefab has no layout element; the wrapped subtitle may overlap the title.");
            Core.Log?.Msg($"[packs] the notice subtitle wraps: {width:0} wide, {extra:0} px taller.");
        }

        private static void HideExpiredNotice()
        {
            if (_noticeRoot != null && Time.unscaledTime >= _noticeUntil) HideNotice($"shown for {NoticeSeconds:0} s");
        }

        private static void HideNotice(string reason)
        {
            if (_noticeRoot == null) return;
            UnityEngine.Object.Destroy(_noticeRoot);
            _noticeRoot = null;
            Core.Log?.Msg($"[packs] notice hidden: {reason}.");
        }

        /// <summary>Queues the notice of the last comparison again, for screenshots (<c>personnel packs notice</c>).</summary>
        internal static bool RequeueLastNotice()
        {
            if (string.IsNullOrEmpty(_lastTitle)) return false;
            Queue(_lastTitle, _lastText);
            return true;
        }

        private static void State(string level, string message)
        {
            if (message == _lastState) return;
            _lastState = message;
            string line = "[packs] " + message;
            if (level == "error") Core.Log?.Error(line);
            else if (level == "warn") Core.Log?.Warning(line);
            else Core.Log?.Msg(line);
        }

        private static string Names(IEnumerable<string> names)
        {
            var list = names.ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        private static string Short(List<string> names)
        {
            if (names.Count <= NoticeNamesShown) return string.Join(", ", names);
            return string.Join(", ", names.Take(NoticeNamesShown)) + $" and {names.Count - NoticeNamesShown} more";
        }

        /// <summary>FNV-1a over the ids in order, as 8 hex digits.</summary>
        private static string Fingerprint(IList<string> parts)
        {
            uint h = 2166136261;
            foreach (string s in parts)
            {
                foreach (char c in s) { h ^= c; h *= 16777619; }
                h ^= '\n'; h *= 16777619;
            }
            return h.ToString("x8");
        }
    }
}
