using System;
using System.IO;
using Personnel.Config;

namespace Personnel.Content
{
    /// <summary>
    /// Writes a small, appearance-only example pack to <c>UserData/Personnel/Packs/Examples</c> when the
    /// <see cref="Preferences.LoadExamplePack"/> toggle is on, so users get a working manifest template to copy.
    /// Generated on disk (no PNGs needed - it uses only scalar appearance knobs). Never overwrites an existing pack.
    /// </summary>
    internal static class ExamplePack
    {
        public static void ExtractIfEnabled()
        {
            if (!Preferences.LoadExamplePack) return;

            string dir = Path.Combine(PackLoader.PacksRoot, "Examples");
            try
            {
                string manifest = Path.Combine(dir, "manifest.json");
                if (File.Exists(manifest))
                {
                    Core.Log?.Msg("Example pack already present - leaving it untouched.");
                    return;
                }

                Directory.CreateDirectory(dir);
                File.WriteAllText(manifest, ExampleManifestJson);
                Core.Log?.Msg($"Wrote example NPC pack -> {dir}");
            }
            catch (Exception ex)
            {
                Core.Log?.Warning($"Example pack write failed: {ex.Message}");
            }
        }

        private const string ExampleManifestJson =
@"{
  ""name"": ""Personnel Examples"",
  ""author"": ""DooDesch"",
  ""npcs"": [
    {
      ""name"": ""Pale"",
      ""appearance"": {
        ""gender"": 0.5,
        ""height"": 1.0,
        ""weight"": 0.4,
        ""skinColor"": ""#8899AA"",
        ""hairPath"": """",
        ""hairColor"": ""#101014"",
        ""eyeBallTint"": ""#FFFFFF"",
        ""pupilDilation"": 0.8
      },
      ""extensions"": {
        ""backrooms"": {
          ""archetype"": ""faceling"",
          ""tierMin"": 1, ""tierMax"": 5,
          ""biomes"": [""L0"", ""L1""],
          ""weight"": 14, ""maxAlive"": 3, ""hostile"": false
        }
      }
    },
    {
      ""name"": ""Ashen"",
      ""appearance"": {
        ""gender"": 0.2,
        ""height"": 1.1,
        ""weight"": 0.6,
        ""skinColor"": ""#4A4A50"",
        ""hairColor"": ""#000000"",
        ""eyeBallTint"": ""#FFCC66""
      },
      ""extensions"": {
        ""backrooms"": {
          ""archetype"": ""wanderer_hollow"",
          ""tierMin"": 3, ""tierMax"": 5,
          ""weight"": 10, ""maxAlive"": 1, ""hostile"": true
        }
      }
    }
  ]
}
";
    }
}
