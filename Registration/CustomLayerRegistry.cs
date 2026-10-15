using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DooDesch.AvatarKit;
using S1API.Rendering;
using UnityEngine;

namespace Personnel.Registration
{
    /// <summary>
    /// Realises custom PNG avatar layers: a built-in tattoo of the matching kind (face or body) is cloned as an
    /// avatar object with the custom texture and registered in the game's avatar object library under the layer
    /// path, which then works as a layer path everywhere, S1API's NPC appearance included
    /// (<see cref="AvatarLook.RegisterCustomLayer"/>). Idempotent + de-duplicated; registration is lazy (first time a
    /// definition's appearance is built).
    /// </summary>
    internal static class CustomLayerRegistry
    {
        // path -> already registered. Value is the resolved Resources path (== the key here) for symmetry.
        private static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // A built-in layer of each kind to clone. Face must route to the face mesh (path contains "/Face/").
        private static string SourceLayer(bool face) =>
            face ? "Avatar/Layers/Tattoos/face/Face_Teardrop" : "Avatar/Layers/Tattoos/chest/Chest_Bird";

        /// <summary>
        /// Ensure a custom layer exists in the registry and return its Resources path (or null on failure).
        /// Provide exactly one of <paramref name="tex"/> (preloaded) or <paramref name="file"/> (pack-relative PNG).
        /// </summary>
        public static string EnsureLayer(string npcSource, string npcId, string packDir, string file, Texture2D tex, bool face)
        {
            string idHint = !string.IsNullOrEmpty(file) ? Path.GetFileNameWithoutExtension(file) : "tex";
            string seg = face ? "Face" : "body";
            string target = "Avatar/Layers/Tattoos/personnel/" + seg + "/" +
                            Sanitize(npcSource) + "_" + Sanitize(npcId) + "_" + Sanitize(idHint);

            if (_resolved.TryGetValue(target, out string done)) return done;

            try
            {
                if (tex == null)
                {
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        Core.Log?.Warning($"Custom layer for '{npcSource}/{npcId}': no texture or file.");
                        return null;
                    }
                    string png = Path.IsPathRooted(file) ? file : Path.Combine(packDir ?? "", file);
                    if (!File.Exists(png))
                    {
                        Core.Log?.Warning($"Custom layer for '{npcSource}/{npcId}': PNG not found at '{png}'.");
                        return null;
                    }
                    tex = TextureUtils.LoadTextureFromFile(png);
                    if (tex == null)
                    {
                        Core.Log?.Warning($"Custom layer for '{npcSource}/{npcId}': failed to load '{png}'.");
                        return null;
                    }
                }

                bool ok = AvatarLook.RegisterCustomLayer(SourceLayer(face), target, npcId ?? idHint, tex);
                if (!ok)
                {
                    Core.Log?.Warning($"Custom layer for '{npcSource}/{npcId}': registering the avatar object failed.");
                    return null;
                }

                _resolved[target] = target;
                Core.Log?.Msg($"Registered custom layer '{npcSource}/{npcId}' ({idHint}) -> {target}");
                return target;
            }
            catch (Exception ex)
            {
                Core.Log?.Warning($"Custom layer for '{npcSource}/{npcId}': registration error - {ex.Message}");
                return null;
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "x";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_');
            return sb.ToString();
        }
    }
}
