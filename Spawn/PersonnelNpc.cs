using Personnel.Model;
using S1API.Entities;

namespace Personnel
{
    /// <summary>
    /// Base class that makes a Personnel NPC definition usable as a real, first-class S1API NPC - "as if you wrote it
    /// yourself with S1API". A consumer mod (e.g. a world-population mod) declares one tiny subclass per NPC it wants,
    /// pointing at a definition id, and spawns it the normal S1API way (<c>new MyNpc()</c>); S1API handles prefab
    /// creation, networking, save/load and world integration. All identity + appearance come from the pack:
    ///
    /// <code>
    ///   // in your mod, referencing Personnel.dll + S1API:
    ///   public sealed class PaleFaceling : Personnel.PersonnelNpc
    ///   {
    ///       protected override string DefId =&gt; "faceling_pale";   // an id from a Personnel pack
    ///   }
    ///   // then, wherever you populate the world:
    ///   var npc = new PaleFaceling();
    ///   npc.Position = spawnPoint;   // place it; give it a schedule, dialogue, etc. via S1API as usual
    /// </code>
    ///
    /// <see cref="DefId"/> must return a compile-time constant: S1API builds the prefab from an UNINITIALIZED instance,
    /// so it may not depend on constructor-set fields.
    /// </summary>
    public abstract class PersonnelNpc : NPC
    {
        /// <summary>The Personnel definition id this NPC realises (must be a constant - no instance state).</summary>
        protected abstract string DefId { get; }

        protected override void ConfigurePrefab(NPCPrefabBuilder builder)
        {
            if (API.TryGet(DefId, out NpcDef def) && def != null)
                API.ConfigureFromDef(builder, def);
            else
                Core.Log?.Warning($"[Personnel] PersonnelNpc: no definition '{DefId}' found - is the pack installed?");
        }
    }
}
