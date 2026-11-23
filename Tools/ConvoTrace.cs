#if DEBUG
using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.Messaging;
using RespList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Messaging.Response>;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only trace of the response buttons of text conversations whose NPC id contains <see cref="Filter"/>:
    /// every load, show and clear is logged with the conversation instance, so a lost deal request after a save
    /// load can be traced to the call that dropped it. Also logs every conversation id that is registered twice.
    /// Off while <see cref="Filter"/> is empty.
    /// </summary>
    internal static class ConvoTrace
    {
        internal static string Filter = "dealprobe";

        // "npcId/conversationId" of a conversation, or the reason it could not be read.
        internal static string Key(MSGConversation c)
        {
            if (c == null) return "null";
            try { return (c._sender?._npcId ?? "?") + "/" + (c.ConversationId ?? "<null>"); }
            catch (Exception e) { return "ids ERR " + e.GetType().Name; }
        }

        internal static bool Match(MSGConversation c, out string key)
        {
            key = Key(c);
            if (string.IsNullOrEmpty(Filter) || c == null) return false;
            int slash = key.IndexOf('/');
            return slash > 0 && key.Substring(0, slash).IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string ConvoId(string key) => key.Contains("/") ? key.Substring(key.IndexOf('/') + 1) : key;

        internal static string Ptr(Il2CppObjectBase o) => o == null ? "null" : "0x" + ((long)o.Pointer).ToString("X");

        internal static int Count(RespList l)
        {
            try { return l?.Count ?? -1; }
            catch (Exception e) { Core.Log?.Warning($"[convotrace] Count threw {e.GetType().Name}"); return -2; }
        }

        internal static MSGConversation Lookup(string conversationId, out string state)
        {
            try
            {
                var mm = Il2CppScheduleOne.DevUtilities.NetworkSingleton<MessagingManager>.Instance;
                if (mm == null) { state = "manager=null"; return null; }
                if (mm.TryGetConversation(conversationId, out MSGConversation c)) { state = "map=" + Ptr(c); return c; }
                state = "map=MISSING";
                return null;
            }
            catch (Exception e) { state = "map=ERR " + e.GetType().Name; return null; }
        }

        internal static string Registered(string key) { Lookup(ConvoId(key), out string s); return s; }

        internal static string Stack()
        {
            try { return new Il2CppSystem.Diagnostics.StackTrace(1, false).ToString().Replace("\n", " | "); }
            catch (Exception e) { return "stack ERR " + e.GetType().Name; }
        }

        internal static void Log(string msg) => Core.Log?.Msg("[convotrace] " + msg);
    }

    [HarmonyPatch(typeof(MSGConversation), nameof(MSGConversation.Load))]
    internal static class ConvoTrace_Load
    {
        private static void Prefix(MSGConversation __instance, Il2CppScheduleOne.Persistence.Datas.MSGConversationData data)
        {
            if (!ConvoTrace.Match(__instance, out string id)) return;
            int saved;
            try { saved = data?.ActiveResponses?.Length ?? -1; } catch { saved = -2; }
            ConvoTrace.Log($"Load {id} this={ConvoTrace.Ptr(__instance)} {ConvoTrace.Registered(id)} savedResponses={saved}");
        }

        private static void Postfix(MSGConversation __instance)
        {
            if (!ConvoTrace.Match(__instance, out string id)) return;
            ConvoTrace.Log($"Load done {id} this={ConvoTrace.Ptr(__instance)} responses={ConvoTrace.Count(__instance.currentResponses)}");
        }
    }

    [HarmonyPatch(typeof(MSGConversation), nameof(MSGConversation.ShowResponses))]
    internal static class ConvoTrace_Show
    {
        private static void Prefix(MSGConversation __instance, RespList _responses, bool network)
        {
            if (!ConvoTrace.Match(__instance, out string id)) return;
            ConvoTrace.Log($"ShowResponses {id} this={ConvoTrace.Ptr(__instance)} {ConvoTrace.Registered(id)} n={ConvoTrace.Count(_responses)} network={network}");
        }
    }

    [HarmonyPatch(typeof(MSGConversation), nameof(MSGConversation.ClearResponses))]
    internal static class ConvoTrace_Clear
    {
        private static void Prefix(MSGConversation __instance, bool network)
        {
            if (!ConvoTrace.Match(__instance, out string id)) return;
            int n = ConvoTrace.Count(__instance.currentResponses);
            ConvoTrace.Log($"ClearResponses {id} this={ConvoTrace.Ptr(__instance)} had={n} network={network}" + (n > 0 ? " stack: " + ConvoTrace.Stack() : ""));
        }
    }

    [HarmonyPatch(typeof(MessagingManager), nameof(MessagingManager.ShowResponses_Client))]
    internal static class ConvoTrace_ShowClient
    {
        private static void Prefix(string id, RespList responses)
        {
            if (string.IsNullOrEmpty(ConvoTrace.Filter) || id == null) return;
            MSGConversation target = ConvoTrace.Lookup(id, out string state);
            if (target == null) { ConvoTrace.Log($"ShowResponses_Client {id} n={ConvoTrace.Count(responses)} {state}"); return; }
            if (!ConvoTrace.Match(target, out string key)) return;
            ConvoTrace.Log($"ShowResponses_Client {key} n={ConvoTrace.Count(responses)} {state}");
        }
    }

    [HarmonyPatch(typeof(MessagingManager), nameof(MessagingManager.Register))]
    internal static class ConvoTrace_Register
    {
        private static void Prefix(MSGConversation conversation)
        {
            bool traced = ConvoTrace.Match(conversation, out string key);
            MSGConversation before = ConvoTrace.Lookup(ConvoTrace.ConvoId(key), out string state);
            if (traced || before != null)
                ConvoTrace.Log($"Register {key} this={ConvoTrace.Ptr(conversation)} before: {state}" +
                               (before != null ? " held by " + ConvoTrace.Key(before) : ""));
        }
    }
}
#endif
