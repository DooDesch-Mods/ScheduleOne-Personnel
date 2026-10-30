using System;
using System.Collections.Generic;
using Personnel.Model;
using UnityEngine;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;
using AvatarDistortion = DooDesch.AvatarKit.AvatarDistortion;

namespace Personnel.Spawn
{
    /// <summary>
    /// Keeps the distortion the game overwrites after spawn. <c>AvatarEffects.Update</c> writes the head bone's scale
    /// every frame, so a head scale or hidden head set once lasts a single frame. The face mesh gets the same treatment
    /// because its hide does not last on a spawned NPC either. Only NPCs that use these keys are tracked, and a culled
    /// avatar costs one property read.
    /// </summary>
    internal static class DistortionKeeper
    {
        private const string HeadKey = "HeadBone";

        private sealed class Entry
        {
            public string Id;
            public Avatar Avatar;
            public Transform Head;
            public Vector3 HeadScale;
            public SkinnedMeshRenderer HiddenFace;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        internal static void Track(Avatar avatar, NpcDef def)
        {
            var distortion = def?.Appearance?.Distortion;
            if (avatar == null || distortion == null) return;

            var entry = new Entry { Id = def.Id, Avatar = avatar };
            if (distortion.TryGetValue(HeadKey, out var head) && head != null)
            {
                entry.Head = avatar._attachmentAnchorProvider != null ? avatar._attachmentAnchorProvider.Head : null;
                if (entry.Head == null) Core.Log?.Warning($"'{def.Id}': no head bone found - its head scale will not last.");
                entry.HeadScale = head.Hide ? Vector3.zero : head.Scale;
            }
            if (distortion.TryGetValue(AvatarDistortion.FaceMeshKey, out var face) && face != null && face.Hide)
            {
                entry.HiddenFace = AvatarDistortion.FaceMesh(avatar);
                if (entry.HiddenFace == null) Core.Log?.Warning($"'{def.Id}': no face mesh found - its hidden face will not last.");
            }
            if (entry.Head == null && entry.HiddenFace == null) return;

            Entries.Add(entry);
            Core.Log?.Msg($"'{def.Id}': head scale and face stay applied every frame ({Entries.Count} NPC(s) tracked).");
        }

        /// <summary>Runs after the frame's Update and Animator passes, before rendering.</summary>
        internal static void LateTick()
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                try
                {
                    if (e.Avatar == null)
                    {
                        Entries.RemoveAt(i);
                        continue;
                    }
                    if (e.Avatar.IsCulled) continue;
                    if (e.Head != null) e.Head.localScale = e.HeadScale;
                    if (e.HiddenFace != null && e.HiddenFace.enabled) e.HiddenFace.enabled = false;
                }
                catch (Exception ex)
                {
                    Core.Log?.Error($"'{e.Id}': keeping the head scale failed ({ex.GetType().Name}: {ex.Message}); the NPC is no longer tracked.");
                    Entries.RemoveAt(i);
                }
            }
        }
    }
}
