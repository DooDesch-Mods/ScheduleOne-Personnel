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
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
