#if DEBUG
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace Personnel.Tools
{
    /// <summary>
    /// <c>personnel hud</c>: logs what decides whether a vanilla notification is on screen - the HUD canvas, the
    /// canvas and canvas groups above the notification list, each queued notification's slide position, the black
    /// overlay and the time scale the slide-in runs on.
    /// </summary>
    internal static class HudProbe
    {
        internal static void Run()
        {
            Core.Log?.Msg($"hud: timeScale {Time.timeScale}");

            var player = Il2CppScheduleOne.PlayerScripts.Player.Local;
            Core.Log?.Msg($"hud: local player {(player == null ? "none" : $"intro completed {player._hasCompletedIntro}")}");
            var intro = Singleton<Il2CppScheduleOne.Cutscenes.IntroManager>.Instance;
            if (intro == null) Core.Log?.Msg("hud: no intro manager.");
            else
            {
                Core.Log?.Msg($"hud: intro object active {intro.gameObject.activeSelf}, playing {intro.IsPlaying}, anim playing {(intro.Anim != null && intro.Anim.isPlaying)}, step {intro.CurrentStep}, container active {(intro.Container != null && intro.Container.activeSelf)}");
                var hidden = intro.DisableDuringIntro;
                if (hidden != null)
                    for (int i = 0; i < hidden.Length; i++)
                        if (hidden[i] != null) Core.Log?.Msg($"hud:   hidden during intro: '{hidden[i].name}' active {hidden[i].activeSelf}");
            }

            var hud = Singleton<HUD>.Instance;
            if (hud == null) Core.Log?.Warning("hud: no HUD instance.");
            else
                Core.Log?.Msg($"hud: HUD active {hud.gameObject.activeInHierarchy}, canvas {(hud.canvas == null ? "null" : $"enabled {hud.canvas.enabled}, mode {hud.canvas.renderMode}, camera {(hud.canvas.worldCamera == null ? "none" : hud.canvas.worldCamera.name + " enabled " + hud.canvas.worldCamera.enabled)}")}, notifications group alpha {(hud.NotificationsCanvasGroup == null ? -1f : hud.NotificationsCanvasGroup.alpha)}");

            var black = Singleton<BlackOverlay>.Instance;
            if (black == null) Core.Log?.Warning("hud: no BlackOverlay instance.");
            else Core.Log?.Msg($"hud: black overlay shown {black.isShown}, alpha {(black.group == null ? -1f : black.group.alpha)}");

            var notifications = Singleton<NotificationsManager>.Instance;
            if (notifications == null) { Core.Log?.Warning("hud: no NotificationsManager instance."); return; }
            var list = notifications.EntryContainer;
            if (list == null) { Core.Log?.Warning("hud: NotificationsManager has no entry container."); return; }
            Core.Log?.Msg($"hud: notification list active {list.gameObject.activeInHierarchy}, {list.childCount} entr(y/ies)");

            for (var t = list.transform; t != null; t = t.parent)
            {
                var canvas = t.GetComponent<Canvas>();
                var group = t.GetComponent<CanvasGroup>();
                if (canvas != null)
                    Core.Log?.Msg($"hud:   canvas '{t.name}' enabled {canvas.enabled}, mode {canvas.renderMode}, order {canvas.sortingOrder}");
                if (group != null)
                    Core.Log?.Msg($"hud:   group '{t.name}' alpha {group.alpha}");
                if (!t.gameObject.activeSelf)
                    Core.Log?.Msg($"hud:   '{t.name}' is inactive (parent '{(t.parent == null ? "scene root" : t.parent.name)}', scene '{t.gameObject.scene.name}')");
            }

            for (int i = 0; i < list.childCount; i++)
            {
                var entry = list.GetChild(i);
                var container = entry.Find("Container")?.TryCast<RectTransform>();
                Core.Log?.Msg($"hud:   entry {i} '{entry.name}' active {entry.gameObject.activeInHierarchy}, x {(container == null ? float.NaN : container.anchoredPosition.x)}");
            }
        }
    }
}
#endif
