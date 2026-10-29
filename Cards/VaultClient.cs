using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MelonLoader.Utils;
using Newtonsoft.Json.Linq;
using Il2CppScheduleOne.DevUtilities;
using NotificationsManager = Il2CppScheduleOne.UI.NotificationsManager;

namespace Personnel.Cards
{
    /// <summary>Progress of <see cref="VaultClient.BeginConnect"/>.</summary>
    public sealed class VaultConnectState
    {
        public enum Phase { Waiting, Connected, Expired, Failed }

        public Phase State;
        /// <summary>The code the player types on the website, e.g. "ABCD-EFGH".</summary>
        public string UserCode;
        /// <summary>The page to open, with the code filled in.</summary>
        public string VerifyUrl;
        public string UserName;
        public string Message;
    }

    /// <summary>Outcome of <see cref="VaultClient.Upload"/>.</summary>
    public sealed class VaultUploadResult
    {
        public bool Ok;
        /// <summary>The card already exists on the vault; <see cref="Code"/> is the original's code.</summary>
        public bool Duplicate;
        public string Code;
        public string Url;
        public string Message;
    }

    /// <summary>
    /// Talks to NPC Vault (personify.doomods.com, API in PersonifyWeb docs/API.md). Connecting a game is a device
    /// flow: the game asks for a code, the player confirms it on the website while logged in, the game receives a
    /// token and keeps it in <c>UserData/Personnel/vault.json</c>. With a token the game uploads cards and checks its
    /// inbox every 30 seconds for cards the player sent from the website. Network work runs on worker threads;
    /// every callback runs on the Unity main thread (drained by <see cref="Pump"/> from Core.OnUpdate).
    /// </summary>
    public static class VaultClient
    {
        public const string DefaultBaseUrl = "https://personify.doomods.com";
        private const int InboxSeconds = 30;

        private static readonly HttpClient Http = MakeClient();
        private static readonly ConcurrentQueue<Action> Completions = new ConcurrentQueue<Action>();
        private static string _token;
        private static int _inboxRunning;
        private static CancellationTokenSource _connectCts;

        /// <summary>Vault address; the MelonPreferences entry "VaultUrl" overrides it for a local test server.</summary>
        public static string BaseUrl => (Config.Preferences.VaultUrl ?? DefaultBaseUrl).TrimEnd('/');

        public static bool IsConnected => !string.IsNullOrEmpty(_token);
        public static string UserName { get; private set; }

        /// <summary>Fired on the main thread when the connection changes (connected, disconnected).</summary>
        public static event Action ConnectionChanged;

        private static string TokenPath => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "vault.json");

        private static readonly string OwnAgent = "Personnel/" + DooDesch.ModVersion.Current;

        // The mod that asked for the current request ("NPCCards/1.0.0"), set by CardsApi.Caller. It flows into the
        // worker task with the execution context, so a request started inside the scope carries it to the end.
        private static readonly AsyncLocal<string> CallerAgent = new AsyncLocal<string>();

        private static HttpClient MakeClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(OwnAgent);
            return c;
        }

        /// <summary>Names the calling mod in the User-Agent of every request started until the scope is disposed.</summary>
        internal static IDisposable CallerScope(string product)
        {
            string before = CallerAgent.Value;
            CallerAgent.Value = product;
            return new Restore(() => CallerAgent.Value = before);
        }

        private sealed class Restore : IDisposable
        {
            private Action _undo;
            public Restore(Action undo) { _undo = undo; }
            public void Dispose() { _undo?.Invoke(); _undo = null; }
        }

        /// <summary>A request whose User-Agent names the calling mod after Personnel, when one is set.</summary>
        private static HttpRequestMessage Request(HttpMethod method, string url)
        {
            var req = new HttpRequestMessage(method, url);
            string caller = CallerAgent.Value;
            if (!string.IsNullOrEmpty(caller)) req.Headers.TryAddWithoutValidation("User-Agent", OwnAgent + " " + caller);
            return req;
        }

        /// <summary>Reads the stored token and starts the inbox check. Called once from Core.</summary>
        internal static void Initialize()
        {
            try
            {
                if (File.Exists(TokenPath))
                {
                    JObject o = JObject.Parse(File.ReadAllText(TokenPath));
                    if (o.Value<string>("baseUrl") == BaseUrl)
                    {
                        _token = o.Value<string>("token");
                        UserName = o.Value<string>("userName");
                        Core.Log?.Msg($"NPC Vault: connected as {UserName ?? "unknown"} ({BaseUrl}).");
                    }
                    else Core.Log?.Msg($"NPC Vault: stored connection is for {o.Value<string>("baseUrl")}, not {BaseUrl}; not connected.");
                }
                else Core.Log?.Msg("NPC Vault: not connected. Type 'personnel connect' in the console to link this game.");
            }
            catch (Exception ex)
            {
                Core.Log?.Warning($"NPC Vault: could not read {TokenPath}: {ex.Message}; not connected.");
            }
            if (IsConnected) StartInboxLoop();
        }

        /// <summary>Runs queued callbacks on the main thread. Bounded per frame.</summary>
        internal static void Pump()
        {
            int guard = 0;
            while (guard++ < 16 && Completions.TryDequeue(out Action a))
            {
                try { a(); }
                catch (Exception ex) { Core.Log?.Warning("NPC Vault: callback threw: " + ex.Message); }
            }
        }

        private static void Post(Action a)
        {
            if (a != null) Completions.Enqueue(a);
        }

        // ---- connect -------------------------------------------------------------------------------------

        /// <summary>
        /// Starts linking this game to a vault account. <paramref name="onUpdate"/> gets the code first (Waiting), then
        /// Connected, Expired or Failed. A second call cancels the first.
        /// </summary>
        public static void BeginConnect(Action<VaultConnectState> onUpdate)
        {
            _connectCts?.Cancel();
            var cts = _connectCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                try
                {
                    JObject start = await SendJson(HttpMethod.Post, "/api/v1/devices/start", new JObject(), false, cts.Token);
                    string deviceCode = start.Value<string>("deviceCode");
                    string userCode = start.Value<string>("userCode");
                    string verify = start.Value<string>("verifyUrl") + "?code=" + Uri.EscapeDataString(userCode);
                    int interval = Math.Max(2, start.Value<int?>("interval") ?? 5);
                    DateTime until = DateTime.UtcNow.AddSeconds(start.Value<int?>("expiresIn") ?? 600);
                    Core.Log?.Msg($"NPC Vault: confirm code {userCode} at {verify}");
                    Post(() => onUpdate?.Invoke(new VaultConnectState { State = VaultConnectState.Phase.Waiting, UserCode = userCode, VerifyUrl = verify }));

                    while (!cts.IsCancellationRequested && DateTime.UtcNow < until)
                    {
                        await Task.Delay(interval * 1000, cts.Token);
                        var (status, body) = await Send(HttpMethod.Post, "/api/v1/devices/poll", new JObject { ["deviceCode"] = deviceCode }, false, cts.Token);
                        if (status == HttpStatusCode.Gone) break;
                        if (status != HttpStatusCode.OK) throw new VaultException(status, body);
                        JObject poll = JObject.Parse(body);
                        if (poll.Value<string>("status") != "ok") continue;

                        _token = poll.Value<string>("token");
                        UserName = poll.Value<string>("userName");
                        SaveToken();
                        Core.Log?.Msg($"NPC Vault: connected as {UserName}.");
                        StartInboxLoop();
                        Post(() =>
                        {
                            onUpdate?.Invoke(new VaultConnectState { State = VaultConnectState.Phase.Connected, UserName = UserName });
                            RaiseConnectionChanged();
                        });
                        return;
                    }
                    if (cts.IsCancellationRequested) { Core.Log?.Msg("NPC Vault: connect cancelled."); return; }
                    Core.Log?.Warning("NPC Vault: the connect code expired before it was confirmed.");
                    Post(() => onUpdate?.Invoke(new VaultConnectState { State = VaultConnectState.Phase.Expired, Message = "The code expired. Start again." }));
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    Core.Log?.Msg("NPC Vault: connect cancelled.");
                }
                catch (Exception ex)
                {
                    Core.Log?.Error("NPC Vault: connect failed: " + Describe(ex));
                    Post(() => onUpdate?.Invoke(new VaultConnectState { State = VaultConnectState.Phase.Failed, Message = Describe(ex) }));
                }
            });
        }

        /// <summary>Forgets the token here and revokes it on the vault.</summary>
        public static void Disconnect(Action<bool> done = null)
        {
            string token = _token;
            _token = null;
            UserName = null;
            try { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
            catch (Exception ex) { Core.Log?.Warning($"NPC Vault: could not delete {TokenPath}: {ex.Message}"); }
            Core.Log?.Msg("NPC Vault: disconnected on this side.");
            RaiseConnectionChanged();
            if (token == null) { done?.Invoke(true); return; }

            Task.Run(async () =>
            {
                bool ok = false;
                try
                {
                    using var req = Request(HttpMethod.Delete, BaseUrl + "/api/v1/devices/me");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var res = await Http.SendAsync(req);
                    ok = res.IsSuccessStatusCode;
                    if (ok) Core.Log?.Msg("NPC Vault: token revoked on the vault.");
                    else Core.Log?.Warning($"NPC Vault: revoke answered {(int)res.StatusCode}; remove the device on the website if it still shows.");
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning("NPC Vault: revoke failed (" + Describe(ex) + "); remove the device on the website if it still shows.");
                }
                Post(() => done?.Invoke(ok));
            });
        }

        // ---- cards ---------------------------------------------------------------------------------------

        /// <summary>GET a vault API path (e.g. "/api/v1/cards?sort=new") and hand back the raw JSON for a UI.</summary>
        public static void GetJson(string pathAndQuery, Action<bool, string> done)
        {
            Task.Run(async () =>
            {
                try
                {
                    var (status, body) = await Send(HttpMethod.Get, pathAndQuery, null, false, CancellationToken.None);
                    if (status != HttpStatusCode.OK) throw new VaultException(status, body);
                    Post(() => done?.Invoke(true, body));
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning($"NPC Vault: GET {pathAndQuery} failed: {Describe(ex)}");
                    Post(() => done?.Invoke(false, Describe(ex)));
                }
            });
        }

        /// <summary>Downloads a card by code and installs it.</summary>
        public static void Install(string code, Action<CardImportResult> done)
        {
            Task.Run(async () =>
            {
                CardImportResult r = await DownloadAndImport(code, "vault " + code);
                Post(() => done?.Invoke(r));
            });
        }

        /// <summary>Publishes a card PNG under the connected account.</summary>
        public static void Upload(byte[] png, Action<VaultUploadResult> done)
        {
            if (!IsConnected)
            {
                Core.Log?.Warning("NPC Vault: upload refused, this game is not connected.");
                done?.Invoke(new VaultUploadResult { Message = "Connect this game to NPC Vault first." });
                return;
            }
            Task.Run(async () =>
            {
                var result = new VaultUploadResult();
                try
                {
                    using var req = Request(HttpMethod.Post, BaseUrl + "/api/v1/cards");
                    req.Content = new ByteArrayContent(png);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                    using var res = await Http.SendAsync(req);
                    string body = await res.Content.ReadAsStringAsync();
                    JObject o = TryParse(body);
                    if (res.StatusCode == HttpStatusCode.Created)
                    {
                        result.Ok = true;
                        result.Code = o?.Value<string>("code");
                        result.Url = o?.Value<string>("url");
                        result.Message = "Published.";
                        Core.Log?.Msg($"NPC Vault: card published as {result.Code}.");
                    }
                    else if (res.StatusCode == HttpStatusCode.Conflict)
                    {
                        result.Duplicate = true;
                        result.Code = o?.Value<string>("code");
                        result.Url = BaseUrl + "/c/" + result.Code;
                        result.Message = "This NPC is already on NPC Vault.";
                        Core.Log?.Msg($"NPC Vault: upload is a duplicate of {result.Code}.");
                    }
                    else
                    {
                        result.Message = o?.Value<string>("message") ?? $"The vault answered {(int)res.StatusCode}.";
                        Core.Log?.Warning($"NPC Vault: upload refused ({(int)res.StatusCode}): {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    result.Message = Describe(ex);
                    Core.Log?.Error("NPC Vault: upload failed: " + result.Message);
                }
                Post(() => done?.Invoke(result));
            });
        }

        // ---- inbox ---------------------------------------------------------------------------------------

        private static void StartInboxLoop()
        {
            if (Interlocked.Exchange(ref _inboxRunning, 1) == 1) return;
            Task.Run(async () =>
            {
                // The loop outlives whichever call started it, so its requests are Personnel's own.
                CallerAgent.Value = null;
                Core.Log?.Msg($"NPC Vault: checking the inbox every {InboxSeconds} s.");
                while (IsConnected)
                {
                    try { await CheckInbox(); }
                    catch (Exception ex) { Core.Log?.Warning("NPC Vault: inbox check failed: " + Describe(ex)); }
                    await Task.Delay(InboxSeconds * 1000);
                }
                Interlocked.Exchange(ref _inboxRunning, 0);
                Core.Log?.Msg("NPC Vault: inbox check stopped (not connected).");
            });
        }

        private static async Task CheckInbox()
        {
            var (status, body) = await Send(HttpMethod.Get, "/api/v1/inbox", null, true, CancellationToken.None);
            if (status == HttpStatusCode.Unauthorized)
            {
                Core.Log?.Warning("NPC Vault: the vault no longer accepts this game's token (revoked on the website?). Disconnecting.");
                Post(() => Disconnect());
                return;
            }
            if (status != HttpStatusCode.OK) throw new VaultException(status, body);

            foreach (JObject item in (JObject.Parse(body)["items"] as JArray ?? new JArray()))
            {
                string id = item.Value<string>("id"), code = item.Value<string>("code");
                CardImportResult r = await DownloadAndImport(code, "inbox " + code);
                // A card that cannot be installed is still acknowledged: it would fail the same way every 30 s.
                var (ackStatus, ackBody) = await Send(HttpMethod.Post, $"/api/v1/inbox/{Uri.EscapeDataString(id)}/ack", null, true, CancellationToken.None);
                if (ackStatus != HttpStatusCode.NoContent && ackStatus != HttpStatusCode.OK)
                    Core.Log?.Warning($"NPC Vault: inbox ack for {code} answered {(int)ackStatus}: {ackBody}");
                else Core.Log?.Msg($"NPC Vault: inbox item {id} ({code}) acknowledged.");
                Post(() => Toast("NPC Vault", r.Ok
                    ? (CardLibrary.PendingRestart && !r.AlreadyInstalled ? r.Message + " Restart to meet them." : r.Message)
                    : "A sent card could not be installed."));
            }
        }

        private static async Task<CardImportResult> DownloadAndImport(string code, string source)
        {
            if (!CardLibrary.IsValidCode(code))
            {
                Core.Log?.Warning($"NPC Vault: refused to download '{code}', not a card code.");
                return new CardImportResult { Error = CardReadError.Invalid, Message = "Not a card code." };
            }
            try
            {
                using var get = Request(HttpMethod.Get, BaseUrl + "/c/" + code + ".png");
                using var res = await Http.SendAsync(get);
                if (!res.IsSuccessStatusCode)
                {
                    Core.Log?.Warning($"NPC Vault: download of {code} answered {(int)res.StatusCode}.");
                    return new CardImportResult { Error = CardReadError.NotPng, Message = $"The vault answered {(int)res.StatusCode} for card {code}." };
                }
                byte[] png = await res.Content.ReadAsByteArrayAsync();
                Core.Log?.Msg($"NPC Vault: downloaded card {code} ({png.Length / 1024} KB).");
                return CardLibrary.Import(png, source, code);
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"NPC Vault: download of {code} failed: {Describe(ex)}");
                return new CardImportResult { Error = CardReadError.NotPng, Message = Describe(ex) };
            }
        }

        // ---- plumbing ------------------------------------------------------------------------------------

        private sealed class VaultException : Exception
        {
            public VaultException(HttpStatusCode status, string body)
                : base($"{(int)status}: {TryParse(body)?.Value<string>("message") ?? Trim(body)}") { }
        }

        private static async Task<(HttpStatusCode, string)> Send(HttpMethod method, string path, JObject json, bool auth, CancellationToken ct)
        {
            using var req = Request(method, BaseUrl + path);
            if (json != null) req.Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            if (auth) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var res = await Http.SendAsync(req, ct);
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
        }

        private static async Task<JObject> SendJson(HttpMethod method, string path, JObject json, bool auth, CancellationToken ct)
        {
            var (status, body) = await Send(method, path, json, auth, ct);
            if ((int)status < 200 || (int)status > 299) throw new VaultException(status, body);
            return JObject.Parse(body);
        }

        private static void SaveToken()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TokenPath));
                File.WriteAllText(TokenPath, new JObject { ["baseUrl"] = BaseUrl, ["token"] = _token, ["userName"] = UserName }.ToString());
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"NPC Vault: could not store the token in {TokenPath}: {ex.Message}. The connection lasts until the game closes.");
            }
        }

        private static JObject TryParse(string body)
        {
            try { return string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body); }
            catch (Exception) { return null; } // not JSON (proxy error page); callers fall back to the raw text
        }

        private static string Trim(string s) => s == null ? "" : s.Length > 200 ? s.Substring(0, 200) + "..." : s;

        private static string Describe(Exception ex) => ex is TaskCanceledException ? "the vault did not answer in time" : ex.GetBaseException().Message;

        private static void RaiseConnectionChanged()
        {
            try { ConnectionChanged?.Invoke(); }
            catch (Exception ex) { Core.Log?.Warning("NPC Vault: ConnectionChanged handler threw: " + ex.Message); }
        }

        /// <summary>Game notification; only shows inside a loaded save.</summary>
        internal static void Toast(string title, string text)
        {
            try
            {
                NotificationsManager n = Singleton<NotificationsManager>.Instance;
                if (n != null) { n.SendNotification(title, text, null, 5f, false); return; }
                Core.Log?.Msg($"NPC Vault (no notification outside a save): {text}");
            }
            catch (Exception ex)
            {
                Core.Log?.Warning($"NPC Vault: notification failed ({ex.Message}): {text}");
            }
        }
    }
}
