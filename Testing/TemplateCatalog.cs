using System;
using System.Collections.Generic;
using System.IO;
using DooDesch.AvatarKit;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personnel.Replacements;
using UnityEngine;
using AvatarSettings = Il2CppScheduleOne.AvatarFramework.AvatarSettings;
using Object = UnityEngine.Object;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1CustomerData = Il2CppScheduleOne.Economy.CustomerData;
using S1Dealer = Il2CppScheduleOne.Economy.Dealer;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;
using S1Supplier = Il2CppScheduleOne.Economy.Supplier;

namespace Personnel.Testing
{
    /// <summary>
    /// Writes <c>UserData/Personify/World/templates.json</c>: every vanilla NPC of the loaded save as a pack entry
    /// (look, clothing, role values, voice) plus whether a card may replace it. An editor in the main menu copies
    /// these into a new NPC or a replacement. A replaced NPC is written with its vanilla values, not the card's.
    /// </summary>
    public static class TemplateCatalog
    {
        public static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "Personify", "World", "templates.json");

        /// <summary>Writes the file from the given vanilla NPCs. Returns false (and logs) on failure.</summary>
        public static bool Write(IEnumerable<S1NPC> vanilla)
        {
            var list = new JArray();
            int failed = 0;
            foreach (S1NPC n in vanilla)
            {
                try { list.Add(Entry(n)); }
                catch (Exception e)
                {
                    failed++;
                    Core.Log?.Warning($"template catalog: '{n?.ID}' could not be read ({e.GetType().Name}: {e.Message}) - left out.");
                }
            }
            var root = new JObject
            {
                ["gameVersion"] = Application.version,
                ["writtenUtc"] = DateTime.UtcNow.ToString("o"),
                ["npcs"] = list,
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, root.ToString(Formatting.Indented));
                Core.Log?.Msg($"template catalog: {list.Count} vanilla NPC(s) written to {FilePath} ({failed} left out).");
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Error($"template catalog could not be written ({e.Message}).");
                return false;
            }
        }

        private static JObject Entry(S1NPC n)
        {
            string name = ((n.FirstName ?? "") + " " + (n.LastName ?? "")).Trim();
            string role = ReplacementRules.RoleOf(n);
            var npc = new JObject
            {
                ["name"] = name.Length > 0 ? name : n.ID,
                ["voice"] = new JObject { ["from"] = n.ID },
            };

            AvatarSettings look = VanillaReplacer.OriginalLook(n.ID);
            bool own = look == null;
            if (own) look = AvatarLook.CaptureSettings(n.Avatar);
            try { if (look != null) npc["appearance"] = Appearance(look); }
            finally { if (own && look != null) Object.Destroy(look); }

            var orig = VanillaReplacer.OriginalOf(n.ID);
            S1CustomerData cd = orig?.CustomerData ?? n.GetComponent<S1Customer>()?.customerData;
            if (cd != null) npc["customer"] = Customer(cd);
            if (role == "dealer")
            {
                var d = n.TryCast<S1Dealer>()?.DealerData;
                npc["dealer"] = new JObject
                {
                    ["cut"] = orig?.Cut ?? d?.SalesCutPercentage ?? 0.2f,
                    ["signingFee"] = orig?.SigningFee ?? d?.SigningFee ?? 0f,
                };
            }
            if (role == "supplier")
            {
                var s = n.TryCast<S1Supplier>()?.SupplierData;
                npc["supplier"] = new JObject
                {
                    ["minOrderLimit"] = orig?.MinOrderLimit ?? s?.MinimumDeaddropOrderLimit ?? 100f,
                    ["maxOrderLimit"] = orig?.MaxOrderLimit ?? s?.MaximumDeaddropOrderLimit ?? 500f,
                };
            }
            if (role == "police") npc["police"] = new JObject();

            return new JObject
            {
                ["id"] = n.ID,
                ["name"] = npc["name"],
                ["role"] = role,
                ["region"] = n.Region.ToString(),
                ["blocked"] = ReplacementRules.BlockReason(n),
                ["replacedBy"] = VanillaReplacer.Applied.TryGetValue(n.ID, out string key) ? key : null,
                ["npc"] = npc,
            };
        }

        private static JObject Appearance(AvatarSettings s)
        {
            var a = new JObject
            {
                ["gender"] = s.Gender, ["height"] = s.Height, ["weight"] = s.Weight,
                ["skinColor"] = Hex(s.SkinColor), ["hairPath"] = s.HairPath ?? "", ["hairColor"] = Hex(s.HairColor),
                ["eyebrowScale"] = s.EyebrowScale, ["eyebrowThickness"] = s.EyebrowThickness,
                ["eyebrowRestingHeight"] = s.EyebrowRestingHeight, ["eyebrowRestingAngle"] = s.EyebrowRestingAngle,
                ["leftEyeLidColor"] = Hex(s.LeftEyeLidColor), ["rightEyeLidColor"] = Hex(s.RightEyeLidColor),
                ["leftEye"] = new JObject { ["top"] = s.LeftEyeRestingState.topLidOpen, ["bottom"] = s.LeftEyeRestingState.bottomLidOpen },
                ["rightEye"] = new JObject { ["top"] = s.RightEyeRestingState.topLidOpen, ["bottom"] = s.RightEyeRestingState.bottomLidOpen },
                ["eyeballMaterial"] = string.IsNullOrEmpty(s.EyeballMaterialIdentifier) ? "Default" : s.EyeballMaterialIdentifier,
                ["eyeBallTint"] = Hex(s.EyeBallTint), ["pupilDilation"] = s.PupilDilation,
            };
            var face = new JArray();
            if (s.FaceLayerSettings != null)
                for (int i = 0; i < s.FaceLayerSettings.Count; i++)
                    if (s.FaceLayerSettings[i] != null) face.Add(new JObject { ["path"] = s.FaceLayerSettings[i].layerPath, ["tint"] = Hex(s.FaceLayerSettings[i].layerTint) });
            var body = new JArray();
            if (s.BodyLayerSettings != null)
                for (int i = 0; i < s.BodyLayerSettings.Count; i++)
                    if (s.BodyLayerSettings[i] != null) body.Add(new JObject { ["path"] = s.BodyLayerSettings[i].layerPath, ["tint"] = Hex(s.BodyLayerSettings[i].layerTint) });
            var acc = new JArray();
            if (s.AccessorySettings != null)
                for (int i = 0; i < s.AccessorySettings.Count; i++)
                    if (s.AccessorySettings[i] != null) acc.Add(new JObject { ["path"] = s.AccessorySettings[i].path, ["color"] = Hex(s.AccessorySettings[i].color) });
            a["faceLayers"] = face;
            a["bodyLayers"] = body;
            a["accessories"] = acc;
            return a;
        }

        private static JObject Customer(S1CustomerData d)
        {
            var c = new JObject
            {
                ["spending"] = new JObject { ["min"] = d.MinWeeklySpend, ["max"] = d.MaxWeeklySpend },
                ["ordersPerWeek"] = new JObject { ["min"] = d.MinOrdersPerWeek, ["max"] = d.MaxOrdersPerWeek },
                ["preferredOrderDay"] = d.PreferredOrderDay.ToString(),
                ["orderTime"] = $"{d.OrderTime / 100:00}:{d.OrderTime % 100:00}",
                ["standards"] = d.Standards.ToString(),
                ["allowDirectApproach"] = d.CanBeDirectlyApproached,
                ["guaranteeFirstSample"] = d.GuaranteeFirstSampleSuccess,
                ["mutualRelationRequirement"] = new JObject { ["min"] = d.MinMutualRelationRequirement, ["max"] = d.MaxMutualRelationRequirement },
                ["callPoliceChance"] = d.CallPoliceChance,
                ["dependence"] = new JObject { ["base"] = d.BaseAddiction, ["multiplier"] = d.DependenceMultiplier },
            };
            var aff = new JObject();
            var list = d.DefaultAffinityData?.ProductAffinities;
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) aff[list[i].DrugType.ToString().ToLowerInvariant()] = list[i].Affinity;
            c["affinities"] = aff;
            var props = new JArray();
            if (d.PreferredProperties != null)
                for (int i = 0; i < d.PreferredProperties.Count; i++)
                    if (d.PreferredProperties[i] != null && !string.IsNullOrEmpty(d.PreferredProperties[i].ID)) props.Add(d.PreferredProperties[i].ID);
            c["preferredProperties"] = props;
            return c;
        }

        private static string Hex(Color c)
        {
            Color32 k = c;
            return k.a == 255 ? $"#{k.r:X2}{k.g:X2}{k.b:X2}" : $"#{k.r:X2}{k.g:X2}{k.b:X2}{k.a:X2}";
        }
    }
}
