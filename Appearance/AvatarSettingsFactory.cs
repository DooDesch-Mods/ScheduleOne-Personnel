using System;
using System.Collections.Generic;
using DooDesch.AvatarKit;
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

            // Face layers are read by POSITION: entry 0 is the mouth, entry 1 is the facial hair and is drawn in the
            // avatar's hair colour whatever tint it carries. A definition lists them in whatever order it likes, so
            // they get sorted into those roles here rather than landing in them by accident.
            AvatarLayerSlots.OrderFaceLayers(faceList);
            foreach (var d in AvatarLayerSlots.TrimFaceToBudget(faceList, BudgetPriority))
                Core.Log?.Warning("[appearance] '" + def.Id + "' exceeds the face layer budget, dropped " + d.layerPath);

            // Only eight body layers ever reach the material; a ninth is written to a slot that does not exist
            // and disappears without a warning. Drop the surplus deliberately and log what went.
            foreach (var d in AvatarLayerSlots.TrimToBudget(bodyList, BudgetPriority))
                Core.Log?.Warning("[appearance] '" + def.Id + "' exceeds the body layer budget, dropped " + d.layerPath);

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
        // Which body layer gives up its slot first when a definition asks for more than the game can render.
        // Custom art is the reason the definition exists, so it outranks stock tattoos, and both outrank clothing.
        private static int BudgetPriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (path.IndexOf("personnel", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (path.IndexOf("/Tattoos/", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
        }

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

            // S1API appends face layers in call order, and the game then reads that list by position (mouth first,
            // facial hair second, drawn in the hair colour) - so the calls have to go out in vanilla's order.
            foreach (var (path, tint) in OrderedFaceLayers(def, a.FaceLayers))
                ab.WithFaceLayer(path, tint);
            // Same eight-layer ceiling as the runtime path below - a ninth body layer is written to a material slot
            // that does not exist and is gone with no error, so the surplus gets dropped on purpose here too.
            var body = new List<(string path, Color tint)>();
            foreach (NpcLayer l in a.BodyLayers ?? new List<NpcLayer>())
            {
                if (l == null) continue;
                string p = ResolvePath(def, l, face: false);
                if (!string.IsNullOrEmpty(p)) body.Add((p, l.Tint));
            }
            TrimByRank(body, AvatarLayerSlots.BodySlots, def.Id, "body");
            foreach (var (path, tint) in body) ab.WithBodyLayer(path, tint);

            foreach (NpcLayer l in a.Accessories)
            {
                if (l != null && !string.IsNullOrWhiteSpace(l.Path)) ab.WithAccessoryLayer(l.Path, l.Tint);
            }
        }

        /// <summary>
        /// The definition's face layers with their paths resolved, sorted into vanilla's fixed roles: mouth first,
        /// facial hair second (an empty entry holds the slot when the NPC has neither), everything else behind them
        /// in its original order and capped at what the face material can render.
        /// </summary>
        private static List<(string path, Color tint)> OrderedFaceLayers(NpcDef def, List<NpcLayer> layers)
        {
            var mouth = new List<(string, Color)>();
            var hair = new List<(string, Color)>();
            var rest = new List<(string, Color)>();
            foreach (NpcLayer l in layers ?? new List<NpcLayer>())
            {
                if (l == null) continue;
                string p = ResolvePath(def, l, face: true);
                if (string.IsNullOrEmpty(p) || p == AvatarLayerSlots.EmptyFacePath) continue;
                if (mouth.Count == 0 && AvatarLayerSlots.IsMouthLayer(p)) mouth.Add((p, l.Tint));
                else if (hair.Count == 0 && AvatarLayerSlots.IsFacialHairLayer(p)) hair.Add((p, l.Tint));
                else rest.Add((p, l.Tint));
            }

            TrimByRank(rest, AvatarLayerSlots.FreeFaceEntries, def.Id, "face");

            var ordered = new List<(string, Color)>
            {
                mouth.Count > 0 ? mouth[0] : (AvatarLayerSlots.EmptyFacePath, Color.white),
                hair.Count > 0 ? hair[0] : (AvatarLayerSlots.EmptyFacePath, Color.white)
            };
            ordered.AddRange(rest);
            return ordered;
        }

        /// <summary>
        /// Cut a resolved layer list down to what the material can render, lowest <see cref="BudgetPriority"/> first
        /// so the same victims go as on the runtime path (<see cref="AvatarLayerSlots.TrimToBudget"/>) - the
        /// definition's own art outranks stock tattoos, and both outrank clothing.
        /// </summary>
        private static void TrimByRank(List<(string path, Color tint)> layers, int max, string id, string what)
        {
            while (layers.Count > max)
            {
                int worst = 0;
                for (int i = 1; i < layers.Count; i++)
                    if (BudgetPriority(layers[i].path) < BudgetPriority(layers[worst].path)) worst = i;
                Core.Log?.Warning("[appearance] '" + id + "' exceeds the " + what + " layer budget, dropped " + layers[worst].path);
                layers.RemoveAt(worst);
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
