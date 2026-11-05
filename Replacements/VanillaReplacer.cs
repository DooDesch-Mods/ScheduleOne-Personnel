using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DooDesch.AvatarKit;
using HarmonyLib;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Personnel.Appearance;
using Personnel.Model;
using UnityEngine;
using AvatarSettings = Il2CppScheduleOne.AvatarFramework.AvatarSettings;
using NPCData = Il2CppScheduleOne.NPCs.Framework.NPCData;
using Object = UnityEngine.Object;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1CustomerData = Il2CppScheduleOne.Economy.CustomerData;
using S1Dealer = Il2CppScheduleOne.Economy.Dealer;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;
using S1Supplier = Il2CppScheduleOne.Economy.Supplier;
using VODatabase = Il2CppScheduleOne.VoiceOver.VODatabase;

namespace Personnel.Replacements
{
    /// <summary>
    /// Puts each replacement (<see cref="ReplacementRegistry"/>) on its vanilla NPC once the save has loaded: look,
    /// clothing, impostor, voice and role values. Name, schedule, home, connections and save data stay vanilla.
    /// Nothing of this is networked or saved by the game, so every peer applies it itself on every load; the contact
    /// picture goes into the NPC's data before it wakes (<see cref="InitializeNpcPatch"/>).
    /// </summary>
    internal static class VanillaReplacer
    {
        /// <summary>What a replaced NPC looked and played like before, for putting it back and for templates.</summary>
        internal sealed class Original
        {
            public AvatarLook.Snapshot Look;
            /// <summary>The vanilla look in AvatarSettings shape (templates); owned here, never destroyed.</summary>
            public AvatarSettings LookSettings;
            public Texture2D Impostor;
            public VODatabase Voice;
            public float Pitch;
            public S1CustomerData CustomerData;
            public float? Cut, SigningFee, MinOrderLimit, MaxOrderLimit;
        }

        private static readonly Dictionary<string, Original> _originals = new Dictionary<string, Original>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _pending;

        /// <summary>Vanilla id -> pack/id of the definition now on it.</summary>
        public static IReadOnlyDictionary<string, string> Applied => _applied;

        /// <summary>The vanilla look of a replaced NPC, or null when it is not replaced.</summary>
        public static AvatarSettings OriginalLook(string vanillaId) =>
            _originals.TryGetValue(vanillaId ?? "", out Original o) ? o.LookSettings : null;

        /// <summary>Original role values of a replaced NPC, or null.</summary>
        public static Original OriginalOf(string vanillaId) => _originals.TryGetValue(vanillaId ?? "", out Original o) ? o : null;

        internal static void OnSceneInitialized(string sceneName)
        {
            if (sceneName != "Main")
            {
                _originals.Clear();
                _applied.Clear();
                return;
            }
            if (_pending) return;
            _pending = true;
            MelonCoroutines.Start(ApplyWhenLoaded());
        }

        private static IEnumerator ApplyWhenLoaded()
        {
            float until = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < until && !(LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded))
                yield return new WaitForSeconds(0.5f);
            _pending = false;
            if (!(LoadManager.InstanceExists && LoadManager.Instance.IsGameLoaded))
            {
                Core.Log?.Warning("[replace] the save did not finish loading within 180 s - no vanilla NPC was replaced.");
                yield break;
            }
            ApplyAll();
        }

        /// <summary>Applies every registered replacement and resets NPCs a removed replacement left behind. Returns the count applied.</summary>
        public static int ApplyAll()
        {
            int ok = 0, total = ReplacementRegistry.All.Count;
            foreach (var kv in ReplacementRegistry.All)
                if (Apply(kv.Key, kv.Value)) ok++;
            TasteState.ResetLeftovers(ReplacementRegistry.All.Keys);
            if (total > 0 || ok > 0) Core.Log?.Msg($"[replace] {ok} of {total} vanilla NPC replacement(s) applied.");
            else Core.Log?.Msg("[replace] no pack replaces a vanilla NPC.");
            return ok;
        }

        /// <summary>Puts <paramref name="def"/> on the vanilla NPC <paramref name="vanillaId"/>. Logs every reason it does not.</summary>
        public static bool Apply(string vanillaId, NpcDef def)
        {
            S1NPC npc = Find(vanillaId);
            if (npc == null) { Core.Log?.Warning($"[replace] '{def.DisplayName}' ({def.Source}): no NPC '{vanillaId}' in this save - skipped."); return false; }
            if (ReplacementRules.BlockReason(npc) is string why) { Core.Log?.Warning($"[replace] '{vanillaId}' is {why} - '{def.DisplayName}' ({def.Source}) not applied."); return false; }
            string npcRole = ReplacementRules.RoleOf(npc), defRole = ReplacementRules.RoleOf(def);
            if (!ReplacementRules.RoleFits(defRole, npcRole))
            {
                Core.Log?.Warning($"[replace] '{def.DisplayName}' ({def.Source}) is a {defRole}, '{vanillaId}' is a {npcRole} - roles differ, not applied.");
                return false;
            }
            if (_applied.ContainsKey(vanillaId)) Restore(vanillaId);

            var orig = Capture(npc);
            _originals[vanillaId] = orig;

            bool look = ApplyLook(npc, def, vanillaId);
            ApplyImpostor(npc, def, vanillaId);
            ApplyVoice(npc, def, vanillaId);
            ApplyRole(npc, def, npcRole, vanillaId);
            _applied[vanillaId] = def.Key;
            Core.Log?.Msg($"[replace] '{vanillaId}' ({npc.FirstName} {npc.LastName}) now looks like '{def.DisplayName}' from pack '{def.Source}' (look {(look ? "applied" : "FAILED")}, role {npcRole}).");
            return look;
        }

        /// <summary>Puts a replaced NPC back to its vanilla look, voice and role values.</summary>
        public static bool Restore(string vanillaId)
        {
            if (!_originals.TryGetValue(vanillaId ?? "", out Original o)) { Core.Log?.Warning($"[replace] restore '{vanillaId}': it is not replaced."); return false; }
            S1NPC npc = Find(vanillaId);
            if (npc == null) { Core.Log?.Warning($"[replace] restore '{vanillaId}': NPC is gone."); return false; }
            bool ok = o.Look != null && AvatarLook.Restore(npc.Avatar, o.Look);
            try { npc.Avatar?.SetImpostorTexture(o.Impostor); } catch (Exception e) { Core.Log?.Warning($"[replace] restore '{vanillaId}': impostor ({e.Message})."); }
            try
            {
                var vo = npc.VoiceOverEmitter;
                if (vo != null && o.Voice != null) { vo.SetDatabase(o.Voice, true); vo.SetDefaultPitch(o.Pitch); }
            }
            catch (Exception e) { Core.Log?.Warning($"[replace] restore '{vanillaId}': voice ({e.Message})."); }
            try
            {
                var cust = npc.GetComponent<S1Customer>();
                if (cust != null && o.CustomerData != null) cust.customerData = o.CustomerData;
                var dealer = npc.TryCast<S1Dealer>();
                if (dealer?.DealerData != null)
                {
                    if (o.Cut.HasValue) dealer.DealerData.SalesCutPercentage = o.Cut.Value;
                    if (o.SigningFee.HasValue) dealer.DealerData.SigningFee = o.SigningFee.Value;
                }
                var sup = npc.TryCast<S1Supplier>();
                if (sup?.SupplierData != null)
                {
                    if (o.MinOrderLimit.HasValue) sup.SupplierData.MinimumDeaddropOrderLimit = o.MinOrderLimit.Value;
                    if (o.MaxOrderLimit.HasValue) sup.SupplierData.MaximumDeaddropOrderLimit = o.MaxOrderLimit.Value;
                }
            }
            catch (Exception e) { Core.Log?.Warning($"[replace] restore '{vanillaId}': role values ({e.Message})."); }
            _applied.Remove(vanillaId);
            Core.Log?.Msg($"[replace] '{vanillaId}' restored to vanilla (look {(ok ? "ok" : "FAILED")}).");
            return ok;
        }

        internal static S1NPC Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            try { return Il2CppScheduleOne.NPCs.NPCManager.GetNPC(id); }
            catch (Exception e) { Core.Log?.Warning($"[replace] looking up '{id}' failed ({e.GetType().Name}: {e.Message})."); return null; }
        }

        private static Original Capture(S1NPC npc)
        {
            var o = new Original();
            try { o.Look = AvatarLook.Capture(npc.Avatar); } catch (Exception e) { Core.Log?.Warning($"[replace] '{npc.ID}': capturing the look failed ({e.Message}) - restore will not work."); }
            try { o.LookSettings = AvatarLook.CaptureSettings(npc.Avatar); } catch (Exception e) { Core.Log?.Warning($"[replace] '{npc.ID}': capturing the look settings failed ({e.Message}) - no template."); }
            try { o.Impostor = npc.NPCData?.Appearance?.Impostor; } catch (Exception e) { Core.Log?.Warning($"[replace] '{npc.ID}': reading the impostor failed ({e.Message})."); }
            try
            {
                var vo = npc.VoiceOverEmitter;
                if (vo != null) { o.Voice = vo._currentDatabase; o.Pitch = vo._defaultPitch; }
            }
            catch (Exception e) { Core.Log?.Warning($"[replace] '{npc.ID}': reading the voice failed ({e.Message})."); }
            try
            {
                o.CustomerData = npc.GetComponent<S1Customer>()?.customerData;
                var dealer = npc.TryCast<S1Dealer>();
                if (dealer?.DealerData != null) { o.Cut = dealer.DealerData.SalesCutPercentage; o.SigningFee = dealer.DealerData.SigningFee; }
                var sup = npc.TryCast<S1Supplier>();
                if (sup?.SupplierData != null) { o.MinOrderLimit = sup.SupplierData.MinimumDeaddropOrderLimit; o.MaxOrderLimit = sup.SupplierData.MaximumDeaddropOrderLimit; }
            }
            catch (Exception e) { Core.Log?.Warning($"[replace] '{npc.ID}': reading role values failed ({e.Message})."); }
            return o;
        }

        private static bool ApplyLook(S1NPC npc, NpcDef def, string id)
        {
            AvatarSettings s = null;
            try
            {
                s = AvatarSettingsFactory.BuildAvatarSettings(def);
                return AvatarLook.Apply(npc.Avatar, s);
            }
            catch (Exception e)
            {
                Core.Log?.Error($"[replace] '{id}': applying the look of '{def.DisplayName}' failed ({e.GetType().Name}: {e.Message}).");
                return false;
            }
            finally { if (s != null) Object.Destroy(s); }
        }

        // The impostor is the flat picture drawn far away. The card has none of its own, so it borrows the one of the
        // NPC whose voice it uses (a template copy names its source there); otherwise the neutral default.
        private static void ApplyImpostor(S1NPC npc, NpcDef def, string id)
        {
            try
            {
                Texture2D tex = null;
                S1NPC src = def.Voice?.From != null ? Find(def.Voice.From) : null;
                if (src != null) tex = src.NPCData?.Appearance?.Impostor;
                if (tex == null) tex = npc.Avatar?._impostor?._defaultImpostorTexture;
                npc.Avatar?.SetImpostorTexture(tex);
                Core.Log?.Msg($"[replace] '{id}': far view uses {(src != null && tex != null ? "the impostor of '" + def.Voice.From + "'" : "the default impostor")}.");
            }
            catch (Exception e) { Core.Log?.Warning($"[replace] '{id}': setting the far view failed ({e.Message}) - it shows the vanilla NPC."); }
        }

        private static void ApplyVoice(S1NPC npc, NpcDef def, string id)
        {
            if (def.Voice == null) { Core.Log?.Msg($"[replace] '{id}': no voice in the card - keeps its own."); return; }
            try
            {
                var vo = npc.VoiceOverEmitter;
                if (vo == null) { Core.Log?.Warning($"[replace] '{id}': has no voice emitter - voice not changed."); return; }
                float? pitch = def.Voice.Pitch;
                if (def.Voice.From != null)
                {
                    S1NPC src = Find(def.Voice.From);
                    var db = src?.VoiceOverEmitter?._currentDatabase;
                    if (db == null) Core.Log?.Warning($"[replace] '{id}': voice source '{def.Voice.From}' not found - keeps its own voice lines.");
                    else
                    {
                        // writeDefault, so effects that reset the voice (zombifying) go back to this one.
                        vo.SetDatabase(db, true);
                        if (!pitch.HasValue) pitch = src.VoiceOverEmitter._defaultPitch;
                    }
                }
                if (pitch.HasValue) vo.SetDefaultPitch(Mathf.Clamp(pitch.Value, 0.1f, 4f));
                Core.Log?.Msg($"[replace] '{id}': voice from '{def.Voice.From ?? id}', pitch {(pitch.HasValue ? pitch.Value.ToString("0.##") : "own")}.");
            }
            catch (Exception e) { Core.Log?.Warning($"[replace] '{id}': changing the voice failed ({e.Message})."); }
        }

        private static void ApplyRole(S1NPC npc, NpcDef def, string role, string id)
        {
            try
            {
                if (def.Customer != null && npc.GetComponent<S1Customer>() is S1Customer cust) ApplyCustomer(cust, def.Customer, id);
                if (role == "dealer" && def.Dealer != null)
                {
                    var data = npc.TryCast<S1Dealer>()?.DealerData;
                    if (data == null) Core.Log?.Warning($"[replace] '{id}': dealer has no dealer data - cut not changed.");
                    else
                    {
                        if (def.Dealer.Cut.HasValue) data.SalesCutPercentage = Mathf.Clamp01(def.Dealer.Cut.Value);
                        if (def.Dealer.SigningFee.HasValue) data.SigningFee = Mathf.Clamp(def.Dealer.SigningFee.Value, 0f, 10000f);
                        Core.Log?.Msg($"[replace] '{id}': dealer cut {data.SalesCutPercentage:0.##}, signing fee {data.SigningFee:0}.");
                        if (def.Dealer.Home != null || def.Dealer.Type != null)
                            Core.Log?.Msg($"[replace] '{id}': home and dealer type stay vanilla.");
                    }
                }
                if (role == "supplier" && def.Supplier != null)
                {
                    var data = npc.TryCast<S1Supplier>()?.SupplierData;
                    if (data == null) Core.Log?.Warning($"[replace] '{id}': supplier has no supplier data - limits not changed.");
                    else
                    {
                        if (def.Supplier.MinOrderLimit.HasValue) data.MinimumDeaddropOrderLimit = Mathf.Max(0f, def.Supplier.MinOrderLimit.Value);
                        if (def.Supplier.MaxOrderLimit.HasValue) data.MaximumDeaddropOrderLimit = Mathf.Max(data.MinimumDeaddropOrderLimit, def.Supplier.MaxOrderLimit.Value);
                        Core.Log?.Msg($"[replace] '{id}': supplier dead drop limits {data.MinimumDeaddropOrderLimit:0}-{data.MaximumDeaddropOrderLimit:0}; assortment stays vanilla.");
                    }
                }
            }
            catch (Exception e) { Core.Log?.Error($"[replace] '{id}': applying role values failed ({e.GetType().Name}: {e.Message})."); }
        }

        // The customer data is an asset the NPC reads live; a copy keeps the asset itself vanilla for a later restore.
        private static void ApplyCustomer(S1Customer cust, NpcCustomer c, string id)
        {
            S1CustomerData src = cust.customerData;
            if (src == null) { Core.Log?.Warning($"[replace] '{id}': customer has no customer data - role values not changed."); return; }
            S1CustomerData d = Object.Instantiate(src);
            d.hideFlags = HideFlags.DontUnloadUnusedAsset;
            d.name = src.name + " (Personnel)";

            if (c.Spending != null) { d.MinWeeklySpend = Mathf.Max(0f, c.Spending.Min); d.MaxWeeklySpend = Mathf.Max(d.MinWeeklySpend, c.Spending.Max); }
            if (c.OrdersPerWeek != null) { d.MinOrdersPerWeek = Mathf.Clamp((int)c.OrdersPerWeek.Min, 0, 7); d.MaxOrdersPerWeek = Mathf.Clamp(Mathf.Max((int)c.OrdersPerWeek.Min, (int)c.OrdersPerWeek.Max), 0, 7); }
            if (!string.IsNullOrWhiteSpace(c.PreferredOrderDay))
            {
                if (Enum.TryParse(c.PreferredOrderDay, true, out Il2CppScheduleOne.GameTime.EDay day)) d.PreferredOrderDay = day;
                else Core.Log?.Warning($"[replace] '{id}': order day '{c.PreferredOrderDay}' unknown - kept.");
            }
            if (c.OrderTime.HasValue) d.OrderTime = Mathf.Clamp(c.OrderTime.Value, 0, 2359);
            if (!string.IsNullOrWhiteSpace(c.Standards))
            {
                if (Enum.TryParse(c.Standards, true, out Il2CppScheduleOne.Economy.ECustomerStandard st)) d.Standards = st;
                else Core.Log?.Warning($"[replace] '{id}': standards '{c.Standards}' unknown - kept.");
            }
            if (c.AllowDirectApproach.HasValue) d.CanBeDirectlyApproached = c.AllowDirectApproach.Value;
            if (c.GuaranteeFirstSample.HasValue) d.GuaranteeFirstSampleSuccess = c.GuaranteeFirstSample.Value;
            if (c.MutualRelationRequirement != null)
            {
                d.MinMutualRelationRequirement = Mathf.Clamp(c.MutualRelationRequirement.Min, 0f, 5f);
                d.MaxMutualRelationRequirement = Mathf.Clamp(c.MutualRelationRequirement.Max, 0f, 5f);
            }
            if (c.CallPoliceChance.HasValue) d.CallPoliceChance = Mathf.Clamp01(c.CallPoliceChance.Value);
            if (c.DependenceBase.HasValue) d.BaseAddiction = Mathf.Clamp01(c.DependenceBase.Value);
            if (c.DependenceMultiplier.HasValue) d.DependenceMultiplier = Mathf.Clamp(c.DependenceMultiplier.Value, 0f, 2f);
            if (c.PreferredProperties != null && c.PreferredProperties.Count > 0) SetProperties(d, c.PreferredProperties, id);

            int taste = 0;
            if (c.Affinities != null && c.Affinities.Count > 0)
            {
                taste = SetAffinities(d.DefaultAffinityData, c.Affinities, id);
                // The save holds the taste as it changed in play; the card's taste replaces it on every load.
                SetAffinities(cust.currentAffinityData, c.Affinities, id);
                TasteState.Mark(id);
            }
            cust.customerData = d;
            Core.Log?.Msg($"[replace] '{id}': customer values set (spend {d.MinWeeklySpend:0}-{d.MaxWeeklySpend:0}, orders {d.MinOrdersPerWeek}-{d.MaxOrdersPerWeek}, day {d.PreferredOrderDay}, standards {d.Standards}, {taste} taste value(s)).");
        }

        private static int SetAffinities(Il2CppScheduleOne.Economy.CustomerAffinityData data, Dictionary<string, float> wanted, string id)
        {
            if (data?.ProductAffinities == null) return 0;
            int n = 0;
            foreach (var kv in wanted)
            {
                if (!Enum.TryParse(kv.Key, true, out Il2CppScheduleOne.Product.EDrugType type))
                {
                    Core.Log?.Warning($"[replace] '{id}': drug type '{kv.Key}' unknown - taste value skipped.");
                    continue;
                }
                bool found = false;
                for (int i = 0; i < data.ProductAffinities.Count; i++)
                {
                    var a = data.ProductAffinities[i];
                    if (a != null && a.DrugType == type) { a.Affinity = Mathf.Clamp(kv.Value, -1f, 1f); found = true; n++; break; }
                }
                if (!found) Core.Log?.Warning($"[replace] '{id}': no taste slot for '{kv.Key}' - skipped.");
            }
            return n;
        }

        private static void SetProperties(S1CustomerData d, List<string> ids, string id)
        {
            var list = new Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Effects.Effect>();
            var missing = new List<string>(ids);
            foreach (string tier in new[] { "Properties/Tier1", "Properties/Tier2", "Properties/Tier3", "Properties/Tier4", "Properties/Tier5" })
            {
                var props = Resources.LoadAll<Il2CppScheduleOne.Effects.Effect>(tier);
                if (props == null) continue;
                foreach (var p in props)
                    if (p != null && missing.RemoveAll(x => string.Equals(x, p.ID, StringComparison.OrdinalIgnoreCase)) > 0) list.Add(p);
            }
            if (missing.Count > 0) Core.Log?.Warning($"[replace] '{id}': unknown preferred properties {string.Join(", ", missing)} - skipped.");
            d.PreferredProperties = list;
        }

        // ---- contact picture -----------------------------------------------------------------------------------

        private static readonly Dictionary<string, Sprite> _portraits = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The contact picture lives in the NPC's data and Messages copies it when the conversation is made, which
        /// happens while the NPC wakes. So the card's portrait goes into the data right before that.
        /// </summary>
        [HarmonyPatch(typeof(S1NPC), nameof(S1NPC.InitializeNPC))]
        internal static class InitializeNpcPatch
        {
            private static void Prefix(NPCData data)
            {
                try
                {
                    string id = data?.BasicInfo?.ID;
                    if (string.IsNullOrEmpty(id) || !ReplacementRegistry.TryGet(id, out NpcDef def)) return;
                    if (ReplacementRules.BlockReason(id) != null) return;
                    if (string.IsNullOrEmpty(def.PortraitPath)) { Core.Log?.Msg($"[replace] '{id}': card has no portrait - contact picture stays vanilla."); return; }
                    Sprite s = Portrait(def);
                    if (s == null) return;
                    data.Appearance.Mugshot = s;
                    Core.Log?.Msg($"[replace] '{id}': contact picture set from '{Path.GetFileName(def.PortraitPath)}'.");
                }
                catch (Exception e) { Core.Log?.Warning($"[replace] contact picture prefix failed ({e.GetType().Name}: {e.Message})."); }
            }
        }

        private static Sprite Portrait(NpcDef def)
        {
            if (_portraits.TryGetValue(def.PortraitPath, out Sprite cached) && cached != null) return cached;
            Texture2D tex = S1API.Rendering.TextureUtils.LoadTextureFromFile(def.PortraitPath);
            if (tex == null) { Core.Log?.Error($"[replace] '{def.Replaces}': portrait '{def.PortraitPath}' could not be loaded."); return null; }
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _portraits[def.PortraitPath] = sprite;
            return sprite;
        }

        // ---- taste left in a save ------------------------------------------------------------------------------

        /// <summary>
        /// Which NPCs of which save got a card's taste written into their live taste, which the game saves. When the
        /// replacement is gone, the NPC's taste goes back to its vanilla default, so removing the pack makes it vanilla.
        /// </summary>
        private static class TasteState
        {
            private static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "replacement-taste.json");

            private static string SaveKey()
            {
                try { return LoadManager.InstanceExists ? LoadManager.Instance.LoadedGameFolderPath ?? "" : ""; }
                catch (Exception e) { Core.Log?.Warning($"[replace] reading the save folder failed ({e.Message})."); return ""; }
            }

            private static Dictionary<string, List<string>> Read()
            {
                try
                {
                    if (!File.Exists(FilePath)) return new Dictionary<string, List<string>>();
                    return JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(FilePath)) ?? new Dictionary<string, List<string>>();
                }
                catch (Exception e) { Core.Log?.Warning($"[replace] {FilePath} unreadable ({e.Message}) - starts empty."); return new Dictionary<string, List<string>>(); }
            }

            private static void Write(Dictionary<string, List<string>> all)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented));
                }
                catch (Exception e) { Core.Log?.Error($"[replace] {FilePath} could not be written ({e.Message})."); }
            }

            public static void Mark(string npcId)
            {
                string key = SaveKey();
                if (key.Length == 0) { Core.Log?.Warning($"[replace] '{npcId}': no save folder known - taste is not tracked for a later reset."); return; }
                var all = Read();
                if (!all.TryGetValue(key, out var ids)) all[key] = ids = new List<string>();
                if (ids.Exists(x => string.Equals(x, npcId, StringComparison.OrdinalIgnoreCase))) return;
                ids.Add(npcId);
                Write(all);
            }

            public static void ResetLeftovers(IEnumerable<string> active)
            {
                string key = SaveKey();
                if (key.Length == 0) return;
                var all = Read();
                if (!all.TryGetValue(key, out var ids) || ids.Count == 0) return;
                var keep = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
                int reset = 0;
                for (int i = ids.Count - 1; i >= 0; i--)
                {
                    string id = ids[i];
                    if (keep.Contains(id)) continue;
                    var cust = Find(id)?.GetComponent<S1Customer>();
                    var def = cust?.customerData?.DefaultAffinityData;
                    if (def?.ProductAffinities == null || cust.currentAffinityData?.ProductAffinities == null)
                    {
                        Core.Log?.Warning($"[replace] '{id}': no longer replaced, but its taste could not be reset (NPC or data missing).");
                        continue;
                    }
                    def.CopyTo(cust.currentAffinityData);
                    ids.RemoveAt(i);
                    reset++;
                    Core.Log?.Msg($"[replace] '{id}': no longer replaced - taste back to vanilla.");
                }
                if (ids.Count == 0) all.Remove(key);
                if (reset > 0) Write(all);
            }
        }
    }
}
