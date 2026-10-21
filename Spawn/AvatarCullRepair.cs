using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;

namespace Personnel.Spawn
{
    /// <summary>
    /// Repairs an NPC avatar that spawns with its body hidden and the flat impostor billboard shown while it is not
    /// culled. The game's culling loop (<c>Avatar.UpdateAnimationActive</c>) only toggles body and impostor when
    /// <c>IsCulled</c> changes, so a clone that copies the culled GameObject state of its source but starts with
    /// <c>IsCulled = false</c> stays a grey silhouette until the camera leaves and re-enters its range. This happens
    /// on the second save load in one game session.
    /// </summary>
    internal static class AvatarCullRepair
    {
        // Past the 3 s after a level load in which the game forces every avatar visible.
        private const float RecheckDelaySeconds = 4f;

        internal static void Watch(GameObject npcObject, string id)
        {
            if (npcObject == null) { Core.Log?.Warning($"'{id}': no NPC object - avatar culling check skipped."); return; }
            Avatar avatar = npcObject.GetComponentInChildren<Avatar>(true);
            if (avatar == null) { Core.Log?.Warning($"'{id}': no avatar found - avatar culling check skipped."); return; }

            RepairIfStuck(avatar, id);
            MelonCoroutines.Start(RecheckLater(avatar, id));
        }

        private static IEnumerator RecheckLater(Avatar avatar, string id)
        {
            yield return new WaitForSeconds(RecheckDelaySeconds);
            if (avatar == null) { Core.Log?.Msg($"'{id}': avatar gone before the culling recheck."); yield break; }
            RepairIfStuck(avatar, id);
        }

        private static void RepairIfStuck(Avatar avatar, string id)
        {
            try
            {
                var body = avatar.BodyContainer;
                var impostor = avatar._impostor;
                if (body == null || impostor == null)
                {
                    Core.Log?.Warning($"'{id}': avatar has no body container or impostor - culling check skipped.");
                    return;
                }

                // SetVisible(false) (entering a building) hides the body without showing the impostor, so an active
                // impostor on an unculled avatar can only be left over from a copied culling state.
                if (avatar.IsCulled || body.gameObject.activeSelf || !impostor.gameObject.activeSelf) return;

                body.gameObject.SetActive(true);
                impostor.Disable();
                Core.Log?.Msg($"'{id}': avatar spawned with the culled body state while visible - body restored.");
            }
            catch (Exception ex)
            {
                Core.Log?.Error($"'{id}': avatar culling repair failed ({ex.Message}).");
            }
        }
    }
}
