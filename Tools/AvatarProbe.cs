#if DEBUG
using System.Text;
using UnityEngine;
using S1Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe: <c>personnel avatar &lt;id-filter&gt;</c>. Logs the render state of an NPC's
    /// avatar - culling flag, body container, impostor billboard and its texture, distance to the camera - so a
    /// body that renders as a flat billboard up close can be told apart from a body with a missing appearance.
    /// </summary>
    internal static class AvatarProbe
    {
        internal static void Run(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) { Log("usage: personnel avatar <id-filter>"); return; }
            var npc = CustomerProbe.Find(filter, out string id);
            if (npc == null) { Log($"avatar: no live NPC matches '{filter}'."); return; }

            S1Avatar avatar = npc.Avatar;
            if (avatar == null) { Log($"avatar {id}: NPC has no Avatar."); return; }

            var sb = new StringBuilder();
            sb.Append($"avatar {id}: go.active={CustomerProbe.Safe(() => avatar.gameObject.activeInHierarchy)}");
            sb.Append($" IsCulled={CustomerProbe.Safe(() => avatar.IsCulled)}");
            sb.Append($" body.activeSelf={CustomerProbe.Safe(() => avatar.BodyContainer.gameObject.activeSelf)}");
            sb.Append($" impostor.activeSelf={CustomerProbe.Safe(() => avatar._impostor.gameObject.activeSelf)}");
            sb.Append($" impostorSet={CustomerProbe.Safe(() => avatar._impostor._impostorSet)}");
            sb.Append($" impostorTex={CustomerProbe.Safe(() => avatar._impostor._meshRenderer.sharedMaterial.mainTexture?.name ?? "<null>")}");
            sb.Append($" camDist={CustomerProbe.Safe(() => Vector3.Distance(Camera.main.transform.position, avatar.transform.position).ToString("0.0"))}");
            Log(sb.ToString());
            Log($"avatar {id}: naked objects = {CustomerProbe.Safe(() => NakedIds(avatar))}");
            Log($"avatar {id}: live objects = {CustomerProbe.Safe(() => LiveIds(avatar))}");
        }

        // What the appearance asked for: the ids in the applied NakedAppearance.
        private static string NakedIds(S1Avatar avatar)
        {
            var naked = avatar.Appearance?.AppliedNakedAppearance;
            if (naked == null) return "<no naked appearance>";
            if (naked.AvatarObjects == null) return "<none>";
            var sb = new StringBuilder();
            foreach (var o in naked.AvatarObjects) sb.Append(o.Id).Append(' ');
            return sb.ToString();
        }

        // What actually exists under the avatar: every AvatarObject component, with its type and active state.
        private static string LiveIds(S1Avatar avatar)
        {
            var sb = new StringBuilder();
            foreach (var o in avatar.GetComponentsInChildren<Il2CppScheduleOne.Core.Avatar.AvatarObject>(true))
                sb.Append($"{o.Id}[{o.PrimaryType},{(o.gameObject.activeInHierarchy ? "on" : "off")}] ");
            return sb.Length == 0 ? "<none>" : sb.ToString();
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
