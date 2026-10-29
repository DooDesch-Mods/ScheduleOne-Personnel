// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Personnel.Cards
{
    /// <summary>
    /// NPC cards for other mods (Personify calls this by reflection, so every member uses plain types: byte arrays,
    /// strings, JSON text and System delegates). A card is a PNG with one NPC in its pixels; see <see cref="CardCodec"/>
    /// for the format and <see cref="CardLibrary"/> for where installed cards live. Vault calls answer on the main
    /// thread.
    /// </summary>
    public static class CardsApi
    {
        /// <summary>Card size in pixels: 900 x 1260.</summary>
        public static int Width => CardCodec.Width;
        public static int Height => CardCodec.Height;

        /// <summary>A fresh card code for meta.json's <c>cardId</c>.</summary>
        public static string NewCode() => CardLibrary.NewCode();

        /// <summary>
        /// Builds the card PNG: <paramref name="rgba"/> is the card picture (8-bit RGBA, rows top to bottom,
        /// <see cref="Width"/> x <see cref="Height"/>), the rest is what the card carries. In <paramref name="npcJson"/>
        /// the portrait is "portrait.png" and custom layer files are "layers/&lt;name&gt;.png", matching
        /// <paramref name="layers"/>. Throws with a readable message when the data does not fit.
        /// </summary>
        public static byte[] WriteCard(byte[] rgba, string npcJson, string metaJson, byte[] portrait, Dictionary<string, byte[]> layers)
        {
            var contents = new CardContents { NpcJson = npcJson, MetaJson = metaJson, Portrait = portrait };
            if (layers != null) foreach (var kv in layers) contents.Layers[kv.Key] = kv.Value;
            byte[] png = CardCodec.Write(rgba, CardCodec.Width, CardCodec.Height, contents);
            Core.Log?.Msg($"Card written: {png.Length / 1024} KB, hash {CardCodec.ContentHash(contents).Substring(0, 12)}.");
            return png;
        }

        /// <summary>Content hash of what a card would carry (meta.json excluded): equal hash = same card.</summary>
        public static string ContentHash(string npcJson, byte[] portrait, Dictionary<string, byte[]> layers)
        {
            var contents = new CardContents { NpcJson = npcJson, Portrait = portrait };
            if (layers != null) foreach (var kv in layers) contents.Layers[kv.Key] = kv.Value;
            return CardCodec.ContentHash(contents);
        }

        /// <summary>
        /// Reads a card. JSON: <c>{ ok, error, message, contentHash, npc, meta, portrait (base64 or null),
        /// layers: { name: base64 } }</c>.
        /// </summary>
        public static string ReadCard(byte[] png)
        {
            CardReadResult r = CardCodec.Read(png);
            if (!r.Ok)
                return new JObject { ["ok"] = false, ["error"] = r.Error.ToString(), ["message"] = r.Message }.ToString();
            var layers = new JObject();
            foreach (var kv in r.Contents.Layers) layers[kv.Key] = Convert.ToBase64String(kv.Value);
            return new JObject
            {
                ["ok"] = true,
                ["contentHash"] = r.ContentHash,
                ["npc"] = JObject.Parse(r.Contents.NpcJson),
                ["meta"] = string.IsNullOrWhiteSpace(r.Contents.MetaJson) ? null : JObject.Parse(r.Contents.MetaJson),
                ["portrait"] = r.Contents.Portrait == null ? null : Convert.ToBase64String(r.Contents.Portrait),
                ["layers"] = layers,
            }.ToString();
        }

        /// <summary>Installs a card PNG. JSON: <c>{ ok, alreadyInstalled, replaced, code, name, npcId, error, message, pendingRestart }</c>.</summary>
        public static string Import(byte[] png, string sourceName) => ToJson(CardLibrary.Import(png, sourceName));

        /// <summary>Installs a card PNG file (see <see cref="Import"/>).</summary>
        public static string ImportFile(string path) => ToJson(CardLibrary.ImportFile(path));

        /// <summary>Installed cards as a JSON array of <c>{ code, name, role, creator, contentHash, npcId, importedAt, basedOn }</c>.</summary>
        public static string List() => new JArray(CardLibrary.List().Select(c => new JObject
        {
            ["code"] = c.Code, ["name"] = c.Name, ["role"] = c.Role, ["creator"] = c.Creator, ["contentHash"] = c.ContentHash,
            ["npcId"] = c.NpcId, ["importedAt"] = c.ImportedAt == default ? null : c.ImportedAt.ToString("o"), ["basedOn"] = c.BasedOn,
        })).ToString();

        public static bool Remove(string code) => CardLibrary.Remove(code);

        /// <summary>True once cards changed since the game started; a restart applies them.</summary>
        public static bool PendingRestart() => CardLibrary.PendingRestart;

        /// <summary>Folder whose PNGs install at the next start.</summary>
        public static string ImportFolder() => CardLibrary.ImportDir;

        /// <summary>JSON <c>{ connected, userName, baseUrl }</c>.</summary>
        public static string VaultState() => new JObject
        {
            ["connected"] = VaultClient.IsConnected, ["userName"] = VaultClient.UserName, ["baseUrl"] = VaultClient.BaseUrl,
        }.ToString();

        /// <summary>Starts the device link; <paramref name="onUpdate"/> gets JSON <c>{ state, userCode, verifyUrl, userName, message }</c>.</summary>
        public static void VaultConnect(Action<string> onUpdate) => VaultClient.BeginConnect(s => onUpdate?.Invoke(new JObject
        {
            ["state"] = s.State.ToString().ToLowerInvariant(), ["userCode"] = s.UserCode, ["verifyUrl"] = s.VerifyUrl,
            ["userName"] = s.UserName, ["message"] = s.Message,
        }.ToString()));

        public static void VaultDisconnect(Action<bool> done) => VaultClient.Disconnect(done);

        /// <summary>GET a vault API path; <paramref name="done"/> gets (ok, body or error text).</summary>
        public static void VaultGet(string pathAndQuery, Action<bool, string> done) => VaultClient.GetJson(pathAndQuery, done);

        /// <summary>Downloads and installs a vault card; <paramref name="done"/> gets the <see cref="Import"/> JSON.</summary>
        public static void VaultInstall(string code, Action<string> done) => VaultClient.Install(code, r => done?.Invoke(ToJson(r)));

        /// <summary>Publishes a card; <paramref name="done"/> gets JSON <c>{ ok, duplicate, code, url, message }</c>.</summary>
        public static void VaultUpload(byte[] png, Action<string> done) => VaultClient.Upload(png, r => done?.Invoke(new JObject
        {
            ["ok"] = r.Ok, ["duplicate"] = r.Duplicate, ["code"] = r.Code, ["url"] = r.Url, ["message"] = r.Message,
        }.ToString()));

        private static string ToJson(CardImportResult r) => new JObject
        {
            ["ok"] = r.Ok, ["alreadyInstalled"] = r.AlreadyInstalled, ["replaced"] = r.Replaced, ["code"] = r.Code,
            ["name"] = r.Name, ["npcId"] = r.NpcId, ["error"] = r.Ok ? null : r.Error.ToString(), ["message"] = r.Message,
            ["pendingRestart"] = CardLibrary.PendingRestart,
        }.ToString();
    }
}
