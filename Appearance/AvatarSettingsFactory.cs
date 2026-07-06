using System.Collections.Generic;
using Il2CppScheduleOne.AvatarFramework;
using Personnel.Model;
using Personnel.Registration;
using S1API.Entities;
using UnityEngine;

namespace Personnel.Appearance
{
    /// <summary>
    /// Realises an <see cref="NpcDef"/>'s appearance into a vanilla <see cref="AvatarSettings"/> ScriptableObject -
    /// the single primitive consumers need. Maps every field the way S1API's <c>NPCPrefabBuilder.WithAppearanceDefaults</c>
    /// does, and registers any custom PNG layers lazily via <see cref="CustomLayerRegistry"/>. The result can be
    /// pushed onto any live <c>Avatar</c> via <c>avatar.LoadAvatarSettings(...)</c>.
    /// </summary>
    public static class AvatarSettingsFactory
    {
        public static AvatarSettings BuildAvatarSettings(NpcDef def)
        {
            if (def == null) return null;
            NpcAppearance a = def.Appearance ?? new NpcAppearance();

            var s = ScriptableObject.CreateInstance<AvatarSettings>();
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;

            s.Gender = a.Gender;
            s.Height = a.Height;
            s.Weight = a.Weight;
            s.SkinColor = a.SkinColor;
            s.HairPath = a.HairPath ?? string.Empty;
            s.HairColor = a.HairColor;
            s.EyebrowScale = a.EyebrowScale;
            s.EyebrowThickness = a.EyebrowThickness;
            s.EyebrowRestingHeight = a.EyebrowRestingHeight;
            s.EyebrowRestingAngle = a.EyebrowRestingAngle;
            s.LeftEyeLidColor = a.LeftEyeLidColor;
            s.RightEyeLidColor = a.RightEyeLidColor;
            s.LeftEyeRestingState = new Eye.EyeLidConfiguration { topLidOpen = a.LeftEyeTop, bottomLidOpen = a.LeftEyeBottom };
            s.RightEyeRestingState = new Eye.EyeLidConfiguration { topLidOpen = a.RightEyeTop, bottomLidOpen = a.RightEyeBottom };
            s.EyeballMaterialIdentifier = string.IsNullOrEmpty(a.EyeballMaterial) ? "Default" : a.EyeballMaterial;
            s.EyeBallTint = a.EyeBallTint;
            s.PupilDilation = a.PupilDilation;

            var faceList = new Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting>();
            var bodyList = new Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting>();
            var accList = new Il2CppSystem.Collections.Generic.List<AvatarSettings.AccessorySetting>();

            AddLayers(def, a.FaceLayers, faceList, face: true);
            AddLayers(def, a.BodyLayers, bodyList, face: false);
            AddAccessories(a.Accessories, accList);

            s.FaceLayerSettings = faceList;
            s.BodyLayerSettings = bodyList;
            s.AccessorySettings = accList;
            return s;
        }

        private static void AddLayers(NpcDef def, List<NpcLayer> layers,
            Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting> dst, bool face)
        {
            if (layers == null) return;
            foreach (NpcLayer l in layers)
            {
                if (l == null) continue;
                string path = ResolvePath(def, l, face);
                if (string.IsNullOrEmpty(path)) continue;
                var ls = new AvatarSettings.LayerSetting();
                ls.layerPath = path;
                ls.layerTint = l.Tint;
                dst.Add(ls);
            }
        }

        // Accessories are meshes attached by asset path (custom accessory meshes are not supported).
        private static void AddAccessories(List<NpcLayer> layers,
            Il2CppSystem.Collections.Generic.List<AvatarSettings.AccessorySetting> dst)
        {
            if (layers == null) return;
            foreach (NpcLayer l in layers)
            {
                if (l == null || string.IsNullOrWhiteSpace(l.Path)) continue;
                var acc = new AvatarSettings.AccessorySetting();
                acc.path = l.Path;
                acc.color = l.Tint;
                dst.Add(acc);
            }
        }

        /// <summary>
        /// Maps a definition's appearance onto S1API's <see cref="NPCPrefabBuilder.AvatarDefaultsBuilder"/> so it can be
        /// baked into a real S1API NPC prefab (see <see cref="PersonnelNpc"/> / <see cref="API.ConfigureFromDef"/>).
        /// Custom PNG layers are registered on first use, same as <see cref="BuildAvatarSettings"/>.
        /// </summary>
        public static void ApplyToDefaults(NPCPrefabBuilder.AvatarDefaultsBuilder ab, NpcDef def)
        {
            if (ab == null || def == null) return;
            NpcAppearance a = def.Appearance ?? new NpcAppearance();

            ab.Gender = a.Gender;
            ab.Height = a.Height;
            ab.Weight = a.Weight;
            ab.SkinColor = a.SkinColor;
            ab.HairPath = a.HairPath ?? string.Empty;
            ab.HairColor = a.HairColor;
            ab.EyebrowScale = a.EyebrowScale;
            ab.EyebrowThickness = a.EyebrowThickness;
            ab.EyebrowRestingHeight = a.EyebrowRestingHeight;
            ab.EyebrowRestingAngle = a.EyebrowRestingAngle;
            ab.LeftEyeLidColor = a.LeftEyeLidColor;
            ab.RightEyeLidColor = a.RightEyeLidColor;
            ab.LeftEye = (a.LeftEyeTop, a.LeftEyeBottom);
            ab.RightEye = (a.RightEyeTop, a.RightEyeBottom);
            ab.EyeballMaterialIdentifier = string.IsNullOrEmpty(a.EyeballMaterial) ? "Default" : a.EyeballMaterial;
            ab.EyeBallTint = a.EyeBallTint;
            ab.PupilDilation = a.PupilDilation;

            foreach (NpcLayer l in a.FaceLayers)
            {
                string p = ResolvePath(def, l, face: true);
                if (!string.IsNullOrEmpty(p)) ab.WithFaceLayer(p, l.Tint);
            }
            foreach (NpcLayer l in a.BodyLayers)
            {
                string p = ResolvePath(def, l, face: false);
                if (!string.IsNullOrEmpty(p)) ab.WithBodyLayer(p, l.Tint);
            }
            foreach (NpcLayer l in a.Accessories)
            {
                if (l != null && !string.IsNullOrWhiteSpace(l.Path)) ab.WithAccessoryLayer(l.Path, l.Tint);
            }
        }

        // Priority: preloaded texture -> custom PNG file -> existing game layer path.
        internal static string ResolvePath(NpcDef def, NpcLayer l, bool face)
        {
            if (l.Texture != null)
                return CustomLayerRegistry.EnsureLayer(def.Source, def.Id, def.PackDir, null, l.Texture, face);
            if (!string.IsNullOrWhiteSpace(l.File))
                return CustomLayerRegistry.EnsureLayer(def.Source, def.Id, def.PackDir, l.File, null, face);
            return l.Path;
        }
    }
}
