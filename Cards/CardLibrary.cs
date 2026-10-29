using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personnel.Content;

namespace Personnel.Cards
{
    /// <summary>Outcome of <see cref="CardLibrary.Import(byte[], string, string)"/>.</summary>
    public sealed class CardImportResult
    {
        public bool Ok;
        /// <summary>The card was already installed with the same content; nothing changed.</summary>
        public bool AlreadyInstalled;
        /// <summary>An older card with the same code was replaced.</summary>
        public bool Replaced;
        public string Code;
        public string Name;
        /// <summary>The definition id the NPC gets ("card_&lt;code&gt;").</summary>
        public string NpcId;
        public CardReadError Error;
        /// <summary>Readable reason on failure, or a short summary on success.</summary>
        public string Message;
    }

    /// <summary>One installed card, as listed by <see cref="CardLibrary.List"/>.</summary>
    public sealed class InstalledCard
    {
        public string Code;
        public string Name;
        public string Role;
        public string Creator;
        public string ContentHash;
        public string NpcId;
        public DateTime ImportedAt;
        /// <summary>Code of the card this one remixes, or null.</summary>
        public string BasedOn;
        /// <summary>How the card came in: "vault &lt;code&gt;", "inbox &lt;code&gt;" or the imported file's name.</summary>
        public string Source;
    }

    /// <summary>
    /// Installs NPC cards into one collection pack, <c>UserData/Personnel/Packs/Cards/</c>. Each card gets its own
    /// folder (<c>&lt;code&gt;/portrait.png</c>, <c>&lt;code&gt;/layers/*.png</c>) and one entry in the pack's
    /// manifest. The NPC id and save id are <c>card_&lt;code&gt;</c>, so two cards never collide and installing a
    /// newer version of the same card keeps the save. Packs load at startup: a card imported while the game runs
    /// takes effect after the next start (<see cref="PendingRestart"/>); PNGs dropped into
    /// <c>UserData/Personnel/Import/</c> are installed at startup before the packs load, so they work right away.
    /// </summary>
    public static class CardLibrary
    {
        public const string PackFolder = "Cards";
        public const string ExtensionKey = "card";
        private const string CodeAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";

        private static readonly object Gate = new object();

        public static string PackDir => Path.Combine(PackLoader.PacksRoot, PackFolder);
        public static string ImportDir => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "Import");

        /// <summary>True once a card was installed or removed after the packs loaded; a restart applies it.</summary>
        public static bool PendingRestart { get; private set; }

        /// <summary>Fired after a card was installed or removed (any thread that changed it).</summary>
        public static event Action Changed;

        private static bool _packsLoaded;

        /// <summary>Called by Core once the packs are loaded; from then on changes need a restart.</summary>
        internal static void MarkPacksLoaded() => _packsLoaded = true;

        /// <summary>True for a 6-character card code from the vault alphabet.</summary>
        public static bool IsValidCode(string code) =>
            code != null && code.Length == 6 && code.All(c => CodeAlphabet.IndexOf(c) >= 0);

        /// <summary>A fresh random card code (Personify stamps it on a new card).</summary>
        public static string NewCode()
        {
            var bytes = new byte[6];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return new string(bytes.Select(b => CodeAlphabet[b % CodeAlphabet.Length]).ToArray());
        }

        /// <summary>Role by the vault's rule: dealer, supplier, customer block, else townsfolk.</summary>
        public static string RoleOf(JObject npc)
        {
            if (npc?["dealer"] is JObject) return "dealer";
            if (npc?["supplier"] is JObject) return "supplier";
            if (npc?["customer"] is JObject) return "customer";
            return "townsfolk";
        }

        /// <summary>Reads and installs a card PNG file.</summary>
        public static CardImportResult ImportFile(string path)
        {
            byte[] png;
            try { png = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                Core.Log?.Warning($"Card import: could not read '{path}': {ex.Message}");
                return new CardImportResult { Error = CardReadError.NotPng, Message = $"Could not read the file: {ex.Message}" };
            }
            return Import(png, Path.GetFileName(path));
        }

        /// <summary>
        /// Reads and installs a card PNG. Never throws; the result says what happened. <paramref name="vaultCode"/>
        /// is the code NPC Vault lists the card under: the vault hands out a new code when the card's own one is
        /// taken by a different card, and installing under the card's own code would replace that other card.
        /// </summary>
        public static CardImportResult Import(byte[] png, string sourceName, string vaultCode = null)
        {
            CardReadResult read = CardCodec.Read(png);
            if (!read.Ok)
            {
                Core.Log?.Warning($"Card import '{sourceName}': {read.Error} - {read.Message}");
                return new CardImportResult { Error = read.Error, Message = read.Message };
            }

            try
            {
                lock (Gate) return Install(read, sourceName, vaultCode);
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"Card import '{sourceName}' failed: {ex}");
                return new CardImportResult { Error = CardReadError.Invalid, Message = "The card could not be installed: " + ex.Message };
            }
        }

        private static CardImportResult Install(CardReadResult read, string sourceName, string vaultCode)
        {
            CardContents c = read.Contents;
            JObject npc = JObject.Parse(c.NpcJson);
            JObject meta = null;
            if (!string.IsNullOrWhiteSpace(c.MetaJson))
            {
                try { meta = JObject.Parse(c.MetaJson); }
                catch (JsonException ex) { Core.Log?.Warning($"Card import '{sourceName}': meta.json is not valid JSON ({ex.Message}); continuing without it."); }
            }

            string code = meta?.Value<string>("cardId");
            if (IsValidCode(vaultCode) && vaultCode != code)
            {
                Core.Log?.Msg($"Card import '{sourceName}': the card calls itself '{code ?? "(none)"}', NPC Vault lists it as '{vaultCode}'; installing as '{vaultCode}'.");
                code = vaultCode;
            }
            if (!IsValidCode(code))
            {
                // A card without its own code gets a stable one from its content, so importing it twice still
                // lands on the same NPC.
                code = new string(Enumerable.Range(0, 6).Select(i => CodeAlphabet[Convert.ToInt32(read.ContentHash.Substring(i * 2, 2), 16) % CodeAlphabet.Length]).ToArray());
                Core.Log?.Msg($"Card import '{sourceName}': no valid card code in meta.json, using '{code}' from its content.");
            }

            JObject manifest = LoadManifest();
            var npcs = (JArray)manifest["npcs"];
            string name = npc.Value<string>("name") ?? npc.Value<string>("id") ?? code;

            foreach (JObject existing in npcs.OfType<JObject>())
            {
                var ext = existing["extensions"]?[ExtensionKey] as JObject;
                if (ext?.Value<string>("contentHash") == read.ContentHash)
                {
                    string have = ext.Value<string>("code");
                    Core.Log?.Msg($"Card import '{sourceName}': '{name}' is already installed as card {have}.");
                    return new CardImportResult
                    {
                        Ok = true, AlreadyInstalled = true, Code = have, Name = name, NpcId = "card_" + have,
                        Message = $"{name} is already installed.",
                    };
                }
            }

            JObject previous = npcs.OfType<JObject>().FirstOrDefault(e => (e["extensions"]?[ExtensionKey] as JObject)?.Value<string>("code") == code);
            string cardDir = Path.Combine(PackDir, code);
            if (Directory.Exists(cardDir)) Directory.Delete(cardDir, true);
            Directory.CreateDirectory(cardDir);

            if (c.Portrait != null)
            {
                File.WriteAllBytes(Path.Combine(cardDir, CardCodec.PortraitEntry), c.Portrait);
                npc["portrait"] = code + "/" + CardCodec.PortraitEntry;
            }
            else npc.Remove("portrait");

            if (c.Layers.Count > 0)
            {
                Directory.CreateDirectory(Path.Combine(cardDir, "layers"));
                foreach (var kv in c.Layers) File.WriteAllBytes(Path.Combine(cardDir, "layers", kv.Key), kv.Value);
            }
            RewriteLayerFiles(npc["appearance"]?["faceLayers"] as JArray, code, c, sourceName);
            RewriteLayerFiles(npc["appearance"]?["bodyLayers"] as JArray, code, c, sourceName);

            // A card holds one NPC; links to other NPCs would point at ids that do not exist here.
            if (npc["relationships"] is JObject rel && rel.Remove("connections"))
                Core.Log?.Msg($"Card import '{sourceName}': dropped relationship connections (a card carries one NPC).");

            string id = "card_" + code;
            npc["id"] = id;
            npc["saveId"] = id;
            var extensions = npc["extensions"] as JObject ?? new JObject();
            extensions[ExtensionKey] = new JObject
            {
                ["code"] = code,
                ["contentHash"] = read.ContentHash,
                ["creator"] = meta?.Value<string>("creator"),
                ["basedOn"] = meta?["basedOn"]?.Type == JTokenType.Object ? meta["basedOn"].Value<string>("code") : null,
                ["importedAt"] = DateTime.UtcNow.ToString("o"),
                ["source"] = sourceName,
            };
            npc["extensions"] = extensions;

            if (previous != null) previous.Replace(npc);
            else npcs.Add(npc);
            SaveManifest(manifest);

            if (_packsLoaded) PendingRestart = true;
            Core.Log?.Msg($"Card import '{sourceName}': installed '{name}' as card {code} (npc {id}, {read.BitsPerChannel}-bit, {c.Layers.Count} layer file(s)){(previous != null ? ", replacing the older version" : "")}.");
            RaiseChanged();
            return new CardImportResult
            {
                Ok = true, Replaced = previous != null, Code = code, Name = name, NpcId = id,
                Message = previous != null ? $"{name} was updated." : $"{name} was added.",
            };
        }

        // Layer files in a card are "layers/<name>.png"; in the pack they live under "<code>/layers/". A layer that
        // points anywhere else (an absolute path, a missing file) is dropped, so a card can only use what it carries.
        private static void RewriteLayerFiles(JArray layers, string code, CardContents c, string sourceName)
        {
            if (layers == null) return;
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                if (!(layers[i] is JObject layer)) continue;
                string file = layer.Value<string>("file");
                if (string.IsNullOrWhiteSpace(file)) continue;
                string bare = file.StartsWith(CardCodec.LayersPrefix, StringComparison.Ordinal) ? file.Substring(CardCodec.LayersPrefix.Length) : null;
                if (bare != null && c.Layers.ContainsKey(bare))
                {
                    layer["file"] = code + "/layers/" + bare;
                    continue;
                }
                Core.Log?.Warning($"Card import '{sourceName}': layer file '{file}' is not inside the card - layer dropped.");
                layers.RemoveAt(i);
            }
        }

        /// <summary>Removes an installed card and its files. Returns false when no card has that code.</summary>
        public static bool Remove(string code)
        {
            try
            {
                lock (Gate)
                {
                    JObject manifest = LoadManifest();
                    var npcs = (JArray)manifest["npcs"];
                    JObject entry = npcs.OfType<JObject>().FirstOrDefault(e => (e["extensions"]?[ExtensionKey] as JObject)?.Value<string>("code") == code);
                    if (entry == null)
                    {
                        Core.Log?.Warning($"Card remove: no installed card with code '{code}'.");
                        return false;
                    }
                    entry.Remove();
                    SaveManifest(manifest);
                    string dir = Path.Combine(PackDir, code);
                    if (IsValidCode(code) && Directory.Exists(dir)) Directory.Delete(dir, true);
                    if (_packsLoaded) PendingRestart = true;
                    Core.Log?.Msg($"Card remove: removed card {code} ('{entry.Value<string>("name")}').");
                }
                RaiseChanged();
                return true;
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"Card remove '{code}' failed: {ex}");
                return false;
            }
        }

        /// <summary>Every installed card, in manifest order.</summary>
        public static List<InstalledCard> List()
        {
            var list = new List<InstalledCard>();
            try
            {
                lock (Gate)
                {
                    foreach (JObject e in ((JArray)LoadManifest()["npcs"]).OfType<JObject>())
                    {
                        var ext = e["extensions"]?[ExtensionKey] as JObject;
                        if (ext == null) continue;
                        DateTime.TryParse(ext.Value<string>("importedAt"), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime at);
                        list.Add(new InstalledCard
                        {
                            Code = ext.Value<string>("code"),
                            Name = e.Value<string>("name"),
                            Role = RoleOf(e),
                            Creator = ext.Value<string>("creator"),
                            ContentHash = ext.Value<string>("contentHash"),
                            NpcId = e.Value<string>("id"),
                            ImportedAt = at,
                            BasedOn = ext.Value<string>("basedOn"),
                            Source = ext.Value<string>("source"),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"Card list: could not read the Cards pack manifest: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Installs every PNG in <see cref="ImportDir"/>; installed files move to <c>Import/Done</c>, rejected ones to
        /// <c>Import/Failed</c> (the log names the reason). Runs at startup before the packs load.
        /// </summary>
        internal static void ImportDropFolder()
        {
            string dir = ImportDir;
            try
            {
                Directory.CreateDirectory(dir);
                string[] files = Directory.GetFiles(dir, "*.png");
                if (files.Length == 0) return;
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                int ok = 0, failed = 0;
                foreach (string f in files)
                {
                    CardImportResult r = ImportFile(f);
                    if (r.Ok) ok++; else failed++;
                    MoveTo(f, Path.Combine(dir, r.Ok ? "Done" : "Failed"));
                }
                Core.Log?.Msg($"Card drop folder: {ok} installed, {failed} rejected ({dir}).");
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"Card drop folder '{dir}' failed: {ex.Message}");
            }
        }

        private static void MoveTo(string file, string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, Path.GetFileName(file));
                if (File.Exists(target))
                    target = Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                File.Move(file, target);
            }
            catch (Exception ex)
            {
                Core.Log?.Warning($"Card drop folder: could not move '{file}' to '{folder}': {ex.Message}");
            }
        }

        private static JObject LoadManifest()
        {
            string path = Path.Combine(PackDir, "manifest.json");
            JObject manifest = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
            if (manifest == null)
            {
                manifest = new JObject
                {
                    ["name"] = "Cards",
                    ["author"] = "NPC cards",
                    ["schemaVersion"] = PackLoader.SchemaVersion,
                    ["packId"] = "cards",
                };
            }
            if (!(manifest["npcs"] is JArray)) manifest["npcs"] = new JArray();
            return manifest;
        }

        private static void SaveManifest(JObject manifest)
        {
            Directory.CreateDirectory(PackDir);
            string path = Path.Combine(PackDir, "manifest.json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, manifest.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Core.Log?.Warning("CardLibrary.Changed handler threw: " + ex.Message); }
        }
    }
}
