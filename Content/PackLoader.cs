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

            // Deterministic pack order: filesystem enumeration is unsorted on some platforms (ext4/Proton),
            // and registration order must match across co-op peers (FishNet spawnables are order-sensitive).
            string[] packDirs = Directory.GetDirectories(root);
            Array.Sort(packDirs, StringComparer.OrdinalIgnoreCase);

            foreach (string packDir in packDirs)
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
                    if (manifest.schemaVersion.HasValue && manifest.schemaVersion.Value > SchemaVersion)
                        Core.Log?.Warning($"Pack '{packName}': schemaVersion {manifest.schemaVersion} is newer than " +
                                          $"this Personnel understands ({SchemaVersion}) - unknown fields will be ignored. " +
                                          "Update Personnel.");

                    int added = 0;
                    var addedIds = new List<string>();
                    foreach (NpcEntry e in manifest.npcs)
                    {
                        NpcDef def = ToDef(packName, packDir, manifest, e);
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

        /// <summary>Highest manifest schemaVersion this loader understands.</summary>
        public const int SchemaVersion = 2;

        private static NpcDef ToDef(string packName, string packDir, NpcPackManifest manifest, NpcEntry e)
        {
            if (e == null) return null;
            string display = !string.IsNullOrWhiteSpace(e.name) ? e.name : e.id;
            if (string.IsNullOrWhiteSpace(display))
            {
                Core.Log?.Warning($"Pack '{packName}': an NPC entry has no 'name' (or 'id') - skipped.");
                return null;
            }

            // Id resolution: an authored id wins (normalized) so ids are stable against folder/name renames;
            // otherwise derive it from the pack identity (manifest packId if set, else the folder name) + name.
            string idPrefix = !string.IsNullOrWhiteSpace(manifest?.packId) ? manifest.packId : packName;
            string derived = Util.Ids.Make(idPrefix, display);
            string authored = Util.Ids.Normalize(e.id);
            string id = !string.IsNullOrEmpty(authored) ? authored : derived;
            if (!string.IsNullOrEmpty(authored) && !string.Equals(authored, derived, StringComparison.Ordinal))
                Core.Log?.Msg($"Pack '{packName}': NPC '{display}' uses its authored id '{authored}' (derived would be '{derived}').");

            var def = new NpcDef
            {
                Id = id,
                SaveId = string.IsNullOrWhiteSpace(e.saveId) ? null : Util.Ids.Normalize(e.saveId),
                DisplayName = display,
                Source = packName,
                PackDir = packDir,
                Appearance = BuildAppearance(e.appearance),
                Behavior = BuildBehavior(e.behavior),
                Spawn = BuildSpawn(e.spawn, manifest, e),
                Contact = BuildContact(e.contact),
                Relationships = BuildRelationships(e.relationships),
                Customer = BuildCustomer(packName, display, e.customer),
                Dealer = BuildDealer(e.dealer),
                Inventory = BuildInventory(packName, display, e.inventory),
                Schedule = BuildSchedule(packName, display, e.schedule),
                Extensions = BuildExtensions(e.extensions)
            };

            // Auto-registration needs a spawn container even if the manifest had no spawn block.
            if (def.Spawn == null && (e.spawn?.auto ?? manifest?.autoRegister ?? false))
                def.Spawn = new NpcSpawn { Auto = true };

            return def;
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
            // Eyelids fall back to the skin colour, not to the tan default: vanilla's creator writes the skin colour
            // into both lids whenever it changes, so a definition that names only a skin colour means matching lids.
            ap.LeftEyeLidColor = ColorParse.Parse(a.leftEyeLidColor, ap.SkinColor);
            ap.RightEyeLidColor = ColorParse.Parse(a.rightEyeLidColor, ap.SkinColor);
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

            if (a.distortion != null)
                foreach (var kv in a.distortion)
                {
                    if (kv.Value == null) continue;
                    ap.Distortion[kv.Key] = new BoneDistortion
                    {
                        Scale = new Vector3(kv.Value.scaleX ?? 1f, kv.Value.scaleY ?? 1f, kv.Value.scaleZ ?? 1f),
                        Hide = kv.Value.hide ?? false
                    };
                }
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

        private static NpcSpawn BuildSpawn(SpawnJson s, NpcPackManifest manifest, NpcEntry e)
        {
            if (s == null) return null;
            var sp = new NpcSpawn
            {
                Region = s.region ?? "",
                RotationY = s.rotationY,
                Physical = s.physical,
                Auto = s.auto ?? manifest?.autoRegister ?? false
            };
            if (s.x.HasValue && s.y.HasValue && s.z.HasValue)
                sp.Position = new Vector3(s.x.Value, s.y.Value, s.z.Value);
            return sp;
        }

        private static NpcContact BuildContact(ContactJson c)
        {
            if (c == null) return null;
            return new NpcContact { Visible = c.visible, MapMarker = c.mapMarker };
        }

        private static NpcRelationships BuildRelationships(RelationshipsJson r)
        {
            if (r == null) return null;
            var rel = new NpcRelationships
            {
                Delta = r.delta,
                Unlocked = r.unlocked,
                UnlockType = string.IsNullOrWhiteSpace(r.unlockType) ? null : r.unlockType
            };
            if (r.connections != null)
            {
                rel.Connections = new List<string>();
                foreach (string c in r.connections)
                    if (!string.IsNullOrWhiteSpace(c)) rel.Connections.Add(c.Trim());
            }
            return rel;
        }

        private static NpcRange BuildRange(MinMaxJson m)
        {
            if (m == null || (!m.min.HasValue && !m.max.HasValue)) return null;
            float min = m.min ?? m.max ?? 0f;
            float max = m.max ?? m.min ?? 0f;
            if (max < min) (min, max) = (max, min);
            return new NpcRange { Min = min, Max = max };
        }

        private static NpcCustomer BuildCustomer(string packName, string npcName, CustomerJson c)
        {
            if (c == null) return null;
            var cu = new NpcCustomer
            {
                Spending = BuildRange(c.spending),
                OrdersPerWeek = BuildRange(c.ordersPerWeek),
                PreferredOrderDay = c.preferredOrderDay,
                Standards = c.standards,
                AllowDirectApproach = c.allowDirectApproach,
                GuaranteeFirstSample = c.guaranteeFirstSample,
                MutualRelationRequirement = BuildRange(c.mutualRelationRequirement),
                CallPoliceChance = c.callPoliceChance,
                DependenceBase = c.dependence?.@base,
                DependenceMultiplier = c.dependence?.multiplier
            };
            if (!string.IsNullOrWhiteSpace(c.orderTime))
            {
                if (Parse.TryParseTime(c.orderTime, out int t)) cu.OrderTime = t;
                else Core.Log?.Warning($"Pack '{packName}': NPC '{npcName}' customer.orderTime '{c.orderTime}' is not a valid time - ignored.");
            }
            if (c.affinities != null && c.affinities.Count > 0)
                cu.Affinities = new Dictionary<string, float>(c.affinities);
            if (c.preferredProperties != null)
            {
                cu.PreferredProperties = new List<string>();
                foreach (string p in c.preferredProperties)
                    if (!string.IsNullOrWhiteSpace(p)) cu.PreferredProperties.Add(p.Trim());
            }
            return cu;
        }

        private static NpcDealer BuildDealer(DealerJson d)
        {
            if (d == null) return null;
            return new NpcDealer
            {
                Type = d.type,
                Cut = d.cut,
                SigningFee = d.signingFee,
                Home = d.home,
                CompletedDealsVariable = d.completedDealsVariable,
                AllowInsufficientQuality = d.allowInsufficientQuality,
                AllowExcessQuality = d.allowExcessQuality
            };
        }

        private static NpcInventory BuildInventory(string packName, string npcName, InventoryJson inv)
        {
            if (inv == null) return null;
            var res = new NpcInventory { Cash = BuildRange(inv.cash), ClearEachNight = inv.clearEachNight };
            if (inv.items != null)
            {
                res.Items = new List<NpcInventoryItem>();
                foreach (JToken t in inv.items)
                {
                    if (t == null) continue;
                    try
                    {
                        if (t.Type == JTokenType.String)
                        {
                            string id = t.Value<string>();
                            if (!string.IsNullOrWhiteSpace(id)) res.Items.Add(new NpcInventoryItem { Id = id.Trim() });
                        }
                        else if (t.Type == JTokenType.Object)
                        {
                            string id = t.Value<string>("id");
                            int qty = t.Value<int?>("quantity") ?? 1;
                            if (!string.IsNullOrWhiteSpace(id))
                                res.Items.Add(new NpcInventoryItem { Id = id.Trim(), Quantity = qty < 1 ? 1 : qty });
                        }
                    }
                    catch (Exception ex)
                    {
                        Core.Log?.Warning($"Pack '{packName}': NPC '{npcName}' has a malformed inventory item ({ex.Message}) - skipped.");
                    }
                }
            }
            return res;
        }

        private static List<NpcScheduleAction> BuildSchedule(string packName, string npcName, List<ScheduleActionJson> actions)
        {
            if (actions == null || actions.Count == 0) return null;
            var list = new List<NpcScheduleAction>();
            foreach (ScheduleActionJson a in actions)
            {
                if (a == null) continue;
                if (string.IsNullOrWhiteSpace(a.type))
                {
                    Core.Log?.Warning($"Pack '{packName}': NPC '{npcName}' has a schedule action without 'type' - skipped.");
                    continue;
                }

                var act = new NpcScheduleAction
                {
                    Type = a.type.Trim(),
                    Name = a.name,
                    FaceDestination = a.faceDestination,
                    Within = a.within,
                    WarpIfSkipped = a.warpIfSkipped,
                    DurationMinutes = a.duration,
                    Building = a.building,
                    DoorIndex = a.doorIndex,
                    SeatSet = a.seatSet,
                    SeatSetPath = a.seatSetPath,
                    IncludeInactive = a.includeInactive,
                    MachineGuid = a.machineGuid,
                    AtmGuid = a.atmGuid,
                    Bet = a.bet,
                    Mode = a.mode,
                    Spins = a.spins,
                    TimeBetweenSpins = a.timeBetweenSpins,
                    MaxSearchDistance = a.maxSearchDistance,
                    GreetingOverride = a.greetingOverride,
                    Choice = a.choice,
                    Action = a.action,
                    EquippablePath = a.equippablePath,
                    GraffitiRegion = a.graffitiRegion,
                    ParkingLot = a.parkingLot,
                    Vehicle = a.vehicle,
                    CreateVehicleCode = a.createVehicle?.code,
                    CreateVehicleRotationY = a.createVehicle?.rotationY
                };

                if (!string.IsNullOrWhiteSpace(a.time))
                {
                    if (Parse.TryParseTime(a.time, out int t)) act.Time = t;
                    else
                    {
                        Core.Log?.Warning($"Pack '{packName}': NPC '{npcName}' schedule action '{a.type}' has invalid time '{a.time}' - action skipped.");
                        continue;
                    }
                }
                if (!string.IsNullOrWhiteSpace(a.endTime))
                {
                    if (Parse.TryParseTime(a.endTime, out int t)) act.EndTime = t;
                    else Core.Log?.Warning($"Pack '{packName}': NPC '{npcName}' schedule action '{a.type}' has invalid endTime '{a.endTime}' - ignored.");
                }
                if (a.position != null)
                {
                    if (Parse.TryParseVec3(a.position, out Vector3 v)) act.Position = v;
                    else
                    {
                        Core.Log?.Warning($"Pack '{packName}': NPC '{npcName}' schedule action '{a.type}' has an invalid position (expected [x,y,z]) - action skipped.");
                        continue;
                    }
                }
                if (a.createVehicle?.position != null)
                {
                    if (Parse.TryParseVec3(a.createVehicle.position, out Vector3 v)) act.CreateVehiclePosition = v;
                    else Core.Log?.Warning($"Pack '{packName}': NPC '{npcName}' schedule action '{a.type}' has an invalid createVehicle.position - ignored.");
                }

                list.Add(act);
            }
            return list.Count > 0 ? list : null;
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

Since Personnel 2.0 an NPC entry can also carry world data - no mod code needed:
  ""spawn"":         { ""x"": 0, ""y"": 0, ""z"": 0, ""rotationY"": 0, ""region"": ""Westville"",
                     ""physical"": true, ""auto"": true }
  ""contact"":       { ""visible"": true, ""mapMarker"": true }
  ""relationships"": { ""delta"": 1.5, ""unlockType"": ""Recommendation"", ""connections"": [""other_npc_id""] }
  ""customer"":      spending/ordersPerWeek/preferredOrderDay/orderTime/standards/affinities/...
  ""dealer"":        type/cut/signingFee/home/...
  ""inventory"":     { ""cash"": { ""min"": 20, ""max"": 120 }, ""items"": [""baggie""], ""clearEachNight"": true }
  ""schedule"":      [ { ""type"": ""walkTo"", ""time"": ""07:30"", ""position"": [x, y, z] },
                     { ""type"": ""stayInBuilding"", ""time"": ""09:00"", ""duration"": 240, ""building"": ""..."" } ]
Set ""autoRegister"": true at the top level (or ""auto"": true per NPC) and Personnel spawns them as real,
networked, saved world NPCs. Times are ""HH:MM"". Full reference:
https://github.com/DooDesch-Mods/ScheduleOne-Personnel/wiki/Pack-Format
");
        }
    }
}
