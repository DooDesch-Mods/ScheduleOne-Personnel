using System;
using HarmonyLib;
using Il2CppScheduleOne.Messaging;
using RespList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Messaging.Response>;

namespace Personnel.Spawn
{
    /// <summary>
    /// Keeps a custom NPC's deal buttons on its own text conversation while the save loads.
    /// S1API loads a custom NPC's save before FishNet spawns it, so its conversation still has the placeholder id
    /// "messageconversation_0" that every unspawned custom contact shares. <c>MSGConversation.Load</c> then calls
    /// <c>ShowResponses</c> with network on, and the messaging RPC looks the id up and finds whichever contact
    /// registered it first: the buttons (Accept, Counteroffer, Reject of an open deal request) end up on that other
    /// conversation and the NPC's own request has none after the load.
    /// When the messaging registry holds a different conversation under this conversation's id, the responses are
    /// shown on this conversation directly. A joining client gets them with the conversation data the host
    /// sends on join.
    /// Belongs in S1API: https://github.com/ifBars/S1API/issues/423. Remove once an S1API release carries it.
    /// </summary>
    [HarmonyPatch(typeof(MSGConversation), nameof(MSGConversation.ShowResponses))]
    internal static class ConversationIdGuard
    {
        private static readonly System.Collections.Generic.HashSet<string> _logged = new System.Collections.Generic.HashSet<string>();

        private static bool Prefix(MSGConversation __instance, RespList _responses, float showResponseDelay, bool network, bool __runOriginal)
        {
            // Personnel's patches can be applied twice; the first copy that redirected the call already did the work.
            if (!__runOriginal || !network || __instance == null) return __runOriginal;

            string id;
            MSGConversation registered;
            try
            {
                id = __instance.ConversationId;
                var manager = Il2CppScheduleOne.DevUtilities.NetworkSingleton<MessagingManager>.Instance;
                if (manager == null || id == null) return true;
                if (!manager.TryGetConversation(id, out registered) || registered == null) return true;
                if (registered.Pointer == __instance.Pointer) return true;
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Conversation guard: could not read the messaging registry ({e.GetType().Name}: {e.Message}); responses sent as usual.");
                return true;
            }

            string npcId = SafeNpcId(__instance);
            try
            {
                __instance.ShowResponses(_responses, showResponseDelay, false);
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Conversation guard: showing {SafeCount(_responses)} response(s) on '{npcId}' failed: {e.GetType().Name}: {e.Message}");
                return false;
            }

            if (_logged.Add(npcId + "|" + id))
                Core.Log?.Msg($"Conversation guard: '{npcId}' shares the conversation id '{id}' with '{SafeNpcId(registered)}' before it spawns - " +
                              $"{SafeCount(_responses)} response(s) kept on its own conversation.");
            return false;
        }

        private static string SafeNpcId(MSGConversation c)
        {
            try { return c?._sender?._npcId ?? "?"; }
            catch (Exception e) { return "? (" + e.GetType().Name + ")"; }
        }

        private static int SafeCount(RespList l)
        {
            try { return l?.Count ?? 0; }
            catch (Exception e) { Core.Log?.Warning($"Conversation guard: response count unreadable ({e.GetType().Name})."); return -1; }
        }
    }
}
