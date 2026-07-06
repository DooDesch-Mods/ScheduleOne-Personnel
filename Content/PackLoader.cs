using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personnel.Model;
using Personnel.Util;
using UnityEngine;

namespace Personnel.Content
{
    /// <summary>
    /// Discovers user NPC packs under <c>UserData/Personnel/Packs/&lt;pack&gt;/manifest.json</c> and turns each entry
    /// into an <see cref="NpcDef"/> (managed data only - no Unity avatar calls; layers are realised lazily later).
    /// </summary>
    internal static class PackLoader
    {
        /// <summary>Root the user drops packs into. Created on first run with a short README.</summary>
        public static string PacksRoot => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "Packs");

        public static List<NpcDef> LoadAll()
        {
            var defs = new List<NpcDef>();
            string root = PacksRoot;

            try
            {
                Directory.CreateDirectory(root);
                WriteReadmeIfMissing(root);
            }
            catch (Exception ex)
            {
                Core.Log?.Warning($"Could not prepare packs folder '{root}': {ex.Message}");
                return defs;
            }

            foreach (string packDir in Directory.GetDirectories(root))
            {
                string manifestPath = Path.Combine(packDir, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                string packName = new DirectoryInfo(packDir).Name;
                try
                {
                    NpcPackManifest manifest = JsonConvert.DeserializeObject<NpcPackManifest>(File.ReadAllText(manifestPath));
                    if (manifest?.npcs == null)
                    {
                        Core.Log?.Warning($"Pack '{packName}': manifest has no 'npcs' array - skipped.");
                        continue;
                    }

                    int added = 0;
                    var addedIds = new List<string>();
                    foreach (NpcEntry e in manifest.npcs)
                    {
                        NpcDef def = ToDef(packName, packDir, e);
                        if (def != null) { defs.Add(def); added++; addedIds.Add(def.Id); }
                    }
                    Core.Log?.Msg($"Pack '{packName}' ({manifest.name ?? "unnamed"}): {added} NPC(s) [{string.Join(", ", addedIds)}].");
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning($"Pack '{packName}': failed to read manifest.json - {ex.Message}");
                }
            }

            return defs;
        }

        private static NpcDef ToDef(string packName, string packDir, NpcEntry e)
        {
            if (e == null) return null;
            // The id is ALWAYS derived (normalized pack_name), never the manifest's own id field - so ids are globally
            // unique and duplicate-proof regardless of how a pack was authored. Falls back to the id only if unnamed.
            string display = !string.IsNullOrWhiteSpace(e.name) ? e.name : e.id;
            if (string.IsNullOrWhiteSpace(display))
            {
                Core.Log?.Warning($"Pack '{packName}': an NPC entry has no 'name' (or 'id') - skipped.");
                return null;
            }

            return new NpcDef
            {
                Id = Util.Ids.Make(packName, display),
                DisplayName = display,
                Source = packName,
                PackDir = packDir,
                Appearance = BuildAppearance(e.appearance),
                Behavior = BuildBehavior(e.behavior),
                Spawn = BuildSpawn(e.spawn),
                Extensions = BuildExtensions(e.extensions)
            };
        }

        private static NpcAppearance BuildAppearance(AppearanceJson a)
        {
            var ap = new NpcAppearance();
            if (a == null) return ap;

            if (a.gender.HasValue) ap.Gender = a.gender.Value;
            if (a.height.HasValue) ap.Height = a.height.Value;
            if (a.weight.HasValue) ap.Weight = a.weight.Value;
            ap.SkinColor = ColorParse.Parse(a.skinColor, ap.SkinColor);
            if (a.hairPath != null) ap.HairPath = a.hairPath;
            ap.HairColor = ColorParse.Parse(a.hairColor, ap.HairColor);
            if (a.eyebrowScale.HasValue) ap.EyebrowScale = a.eyebrowScale.Value;
            if (a.eyebrowThickness.HasValue) ap.EyebrowThickness = a.eyebrowThickness.Value;
            if (a.eyebrowRestingHeight.HasValue) ap.EyebrowRestingHeight = a.eyebrowRestingHeight.Value;
            if (a.eyebrowRestingAngle.HasValue) ap.EyebrowRestingAngle = a.eyebrowRestingAngle.Value;
            ap.LeftEyeLidColor = ColorParse.Parse(a.leftEyeLidColor, ap.LeftEyeLidColor);
            ap.RightEyeLidColor = ColorParse.Parse(a.rightEyeLidColor, ap.RightEyeLidColor);
            if (a.leftEye != null)
            {
                if (a.leftEye.top.HasValue) ap.LeftEyeTop = a.leftEye.top.Value;
                if (a.leftEye.bottom.HasValue) ap.LeftEyeBottom = a.leftEye.bottom.Value;
            }
            if (a.rightEye != null)
            {
                if (a.rightEye.top.HasValue) ap.RightEyeTop = a.rightEye.top.Value;
                if (a.rightEye.bottom.HasValue) ap.RightEyeBottom = a.rightEye.bottom.Value;
            }
            if (!string.IsNullOrWhiteSpace(a.eyeballMaterial)) ap.EyeballMaterial = a.eyeballMaterial;
            ap.EyeBallTint = ColorParse.Parse(a.eyeBallTint, ap.EyeBallTint);
            if (a.pupilDilation.HasValue) ap.PupilDilation = a.pupilDilation.Value;

            AppendLayers(a.faceLayers, ap.FaceLayers);
            AppendLayers(a.bodyLayers, ap.BodyLayers);
            AppendLayers(a.accessories, ap.Accessories);
            return ap;
        }

        private static void AppendLayers(List<LayerJson> src, List<NpcLayer> dst)
        {
            if (src == null) return;
            foreach (LayerJson l in src)
            {
                if (l == null) continue;
                if (string.IsNullOrWhiteSpace(l.path) && string.IsNullOrWhiteSpace(l.file)) continue;
                dst.Add(new NpcLayer
                {
                    Path = l.path,
                    File = l.file,
                    Tint = ColorParse.Parse(l.tint ?? l.color, Color.white)
                });
            }
        }

        private static NpcBehavior BuildBehavior(BehaviorJson b)
        {
            if (b == null) return null;
            var beh = new NpcBehavior();
            if (b.aggression.HasValue) beh.Aggression = b.aggression.Value;
            if (b.maxHealth.HasValue) beh.MaxHealth = b.maxHealth.Value;
            if (b.scale.HasValue) beh.Scale = b.scale.Value;
            if (!string.IsNullOrWhiteSpace(b.conversation)) beh.Conversation = b.conversation;
            return beh;
        }

        private static NpcSpawn BuildSpawn(SpawnJson s)
        {
            if (s == null) return null;
            var sp = new NpcSpawn { Region = s.region ?? "" };
            if (s.x.HasValue && s.y.HasValue && s.z.HasValue)
                sp.Position = new Vector3(s.x.Value, s.y.Value, s.z.Value);
            return sp;
        }

        private static IReadOnlyDictionary<string, string> BuildExtensions(JObject ext)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ext == null) return dict;
            foreach (JProperty p in ext.Properties())
            {
                try { dict[p.Name] = p.Value.ToString(Formatting.None); }
                catch { /* skip a malformed block, keep the rest */ }
            }
            return dict;
        }

        private static void WriteReadmeIfMissing(string root)
        {
            string readme = Path.Combine(root, "README.txt");
            if (File.Exists(readme)) return;
            File.WriteAllText(readme,
@"Personnel - custom NPC packs
============================

Drop one folder per pack in this directory. Each pack needs a manifest.json and (optionally) the PNG files
its custom layers reference. Build packs visually with the ""Personify"" editor (Side Hustle gamemode).

Folder layout:
  Packs/
    MyPack/
      manifest.json
      grin.png            (optional custom layer texture)

manifest.json:
{
  ""name"": ""My Pack"",
  ""author"": ""you"",
  ""npcs"": [
    {
      ""id"": ""faceling_pale"",
      ""name"": ""Pale Faceling"",
      ""appearance"": {
        ""gender"": 0.5, ""height"": 1.0, ""weight"": 0.4, ""skinColor"": ""#8899AA"",
        ""hairPath"": """", ""hairColor"": ""#000000"",
        ""eyeBallTint"": ""#FFFFFF"", ""pupilDilation"": 1,
        ""faceLayers"": [ { ""file"": ""grin.png"", ""kind"": ""face"", ""tint"": ""#FFFFFF"" } ],
        ""bodyLayers"": [ { ""path"": ""Avatar/Layers/Body/..."", ""tint"": ""#334455"" } ],
        ""accessories"": [ ]
      },
      ""extensions"": {
        ""backrooms"": { ""archetype"": ""faceling"", ""tierMin"": 1, ""tierMax"": 5,
                         ""biomes"": [""L0"",""L1""], ""weight"": 14, ""maxAlive"": 3, ""hostile"": false }
      }
    }
  ]
}

Layers:  use ""path"" to reference an existing in-game layer, OR ""file"" (a PNG next to manifest.json)
         with ""kind"": face|body for a custom layer. Custom PNGs must match the game's body/face UV.
extensions: free-form per-consumer data. The Backrooms mod reads the ""backrooms"" block; other mods
         ignore it.
");
        }
    }
}
