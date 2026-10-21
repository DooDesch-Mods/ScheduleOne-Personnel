using System;
using System.IO;
using Personnel.Config;

namespace Personnel.Content
{
    /// <summary>
    /// Writes a small, appearance-only example pack to <c>UserData/Personnel/Packs/Examples</c> when the
    /// <see cref="Preferences.LoadExamplePack"/> toggle is on, so users get a working manifest template to copy.
    /// Generated on disk (no PNGs needed - it uses only scalar appearance knobs). An existing pack is only replaced
    /// when it is byte-for-byte an older bundled version; a pack the user edited is never touched.
    /// </summary>
    internal static class ExamplePack
    {
        // SHA-256 of earlier bundled manifests (line endings normalised to \n, trimmed). 1.0.0 shipped Pale only;
        // 2.0.0 to 2.2.2 shipped Errand Eddie with "auto": false, a spawn 4 m under the street in the wrong
        // region and a stay in "Thrifty Threads", which is not an NPC building.
        private static readonly string[] OutdatedManifestHashes =
        {
            "f0a5229989a590a9cfccc519abfa75f2a77741956f7690a1f6634b616b4ef090",
            "9503e19b14c4065dad28317944633558f9c8460dd1e0980c80050e41c2e828d3",
        };

        public static void ExtractIfEnabled()
        {
            if (!Preferences.LoadExamplePack) return;

            string dir = Path.Combine(PackLoader.PacksRoot, "Examples");
            try
            {
                string manifest = Path.Combine(dir, "manifest.json");
                if (File.Exists(manifest))
                {
                    string hash = NormalisedHash(File.ReadAllText(manifest));
                    if (hash == NormalisedHash(ExampleManifestJson))
                    {
                        Core.Log?.Msg("Example pack already present and current - leaving it untouched.");
                        return;
                    }
                    if (Array.IndexOf(OutdatedManifestHashes, hash) < 0)
                    {
                        Core.Log?.Msg("Example pack already present and edited by you - leaving it untouched.");
                        return;
                    }

                    File.WriteAllText(manifest, ExampleManifestJson);
                    Core.Log?.Msg($"Replaced an outdated bundled example pack -> {dir}");
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

        private static string NormalisedHash(string text)
        {
            string normalised = text.Replace("\r\n", "\n").Trim();
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] digest = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalised));
            return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
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
    },
    {
      ""name"": ""Errand Eddie"",
      ""appearance"": {
        ""gender"": 0.0,
        ""height"": 1.0,
        ""weight"": 0.5,
        ""skinColor"": ""#C09070"",
        ""hairColor"": ""#3A2A1A""
      },
      ""spawn"": {
        ""x"": -64.25, ""y"": 1.07, ""z"": 84.49,
        ""rotationY"": 325.0,
        ""region"": ""Northtown"",
        ""physical"": true,
        ""auto"": true
      },
      ""contact"": { ""visible"": true, ""mapMarker"": true },
      ""relationships"": { ""delta"": 1.5, ""unlockType"": ""Recommendation"" },
      ""customer"": {
        ""spending"": { ""min"": 300, ""max"": 900 },
        ""ordersPerWeek"": { ""min"": 1, ""max"": 3 },
        ""preferredOrderDay"": ""Friday"",
        ""orderTime"": ""19:30"",
        ""standards"": ""Moderate"",
        ""affinities"": { ""marijuana"": 0.6 }
      },
      ""inventory"": { ""cash"": { ""min"": 20, ""max"": 120 }, ""clearEachNight"": true },
      ""schedule"": [
        { ""type"": ""stayInBuilding"", ""time"": ""09:00"", ""duration"": 240, ""building"": ""Motel Office"" },
        { ""type"": ""walkTo"", ""time"": ""14:00"", ""position"": [-64.25, 1.07, 84.49] }
      ]
    }
  ]
}
";
    }
}
