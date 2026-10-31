using System;
using Il2CppScheduleOne.AvatarFramework;
using Personnel.Model;
using Personnel.Util;
using S1API.Entities;

namespace Personnel
{
    /// <summary>
    /// Base class that makes a Personnel NPC definition usable as a real, first-class S1API NPC - "as if you wrote it
    /// yourself with S1API". A consumer mod (e.g. a world-population mod) declares one tiny subclass per NPC it wants,
    /// pointing at a definition id, and spawns it the normal S1API way (<c>new MyNpc()</c>); S1API handles prefab
    /// creation, networking, save/load and world integration. Identity, appearance, role, economy, schedule and
    /// spawn all come from the pack:
    ///
    /// <code>
    ///   // in your mod, referencing Personnel.dll + S1API:
    ///   public sealed class PaleFaceling : Personnel.PersonnelNpc
    ///   {
    ///       protected override string DefId =&gt; "faceling_pale";   // an id from a Personnel pack
    ///   }
    ///   // then, wherever you populate the world:
    ///   var npc = new PaleFaceling();
    /// </code>
    ///
    /// Packs with <c>autoRegister</c> (or per-NPC <c>spawn.auto</c>) don't even need the subclass - Personnel
    /// generates it at runtime.
    ///
    /// <see cref="DefId"/> must return a compile-time constant: S1API calls <see cref="ConfigurePrefab"/>,
    /// <see cref="IsDealer"/> and <see cref="IsPhysical"/> on an UNINITIALIZED instance, so none of them may
    /// depend on constructor-set fields.
    /// </summary>
    public abstract class PersonnelNpc : NPC
    {
        /// <summary>The Personnel definition id this NPC realises (must be a constant - no instance state).</summary>
        protected abstract string DefId { get; }

        /// <summary>
        /// Def-driven: true when the definition has a dealer{} block (or the legacy
        /// behavior.conversation="dealer"). S1API reads this BEFORE ConfigurePrefab to pick the dealer vs.
        /// civilian base prefab, on an uninitialized instance - it must never throw, or the NPC silently
        /// gets the wrong prefab. Override if your mod decides the role itself.
        /// </summary>
        public override bool IsDealer
        {
            get
            {
                try
                {
                    return API.TryGet(DefId, out NpcDef def) && API.ResolveRole(def) == "dealer";
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning($"IsDealer lookup for '{GetType().Name}' threw ({ex.Message}) - assuming civilian.");
                    return false;
                }
            }
        }

        /// <summary>
        /// Def-driven: true when the definition has a supplier{} block (and no dealer{} block). Read by S1API before
        /// ConfigurePrefab to pick the supplier base prefab, on an uninitialized instance - must never throw.
        /// </summary>
        public override bool IsSupplier
        {
            get
            {
                try
                {
                    return API.TryGet(DefId, out NpcDef def) && API.ResolveRole(def) == "supplier";
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning($"IsSupplier lookup for '{GetType().Name}' threw ({ex.Message}) - assuming no supplier.");
                    return false;
                }
            }
        }

        /// <summary>
        /// Def-driven: spawn.physical when set, otherwise physical exactly when the definition has a
        /// schedule. Default false = phone-contact only (no world body, no pathing cost - the right choice
        /// for most roster NPCs). Override if your mod decides this itself.
        /// </summary>
        public override bool IsPhysical
        {
            get
            {
                try
                {
                    if (!API.TryGet(DefId, out NpcDef def) || def == null) return false;
                    if (API.ResolveRole(def) == "supplier") return true;
                    return def.Spawn?.Physical ?? (def.Schedule != null && def.Schedule.Count > 0);
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning($"IsPhysical lookup for '{GetType().Name}' threw ({ex.Message}) - assuming not physical.");
                    return false;
                }
            }
        }

        protected override void ConfigurePrefab(NPCPrefabBuilder builder)
        {
            if (API.TryGet(DefId, out NpcDef def) && def != null)
                API.ConfigureFromDef(builder, def);
            else
                Core.Log?.Warning($"PersonnelNpc: no definition '{DefId}' found - is the pack installed?");
        }

        protected override void OnCreated()
        {
            base.OnCreated();
            Spawn.AvatarCullRepair.Watch(gameObject, DefId);
            if (!API.TryGet(DefId, out NpcDef def) || def == null)
            {
                API.AddMapMarker(gameObject);
                return;
            }

            // AvatarSettings (applied in ConfigurePrefab -> WithAppearanceDefaults) can't express bone
            // distortion, so it's applied here as a separate pass once a real Avatar exists. A failure here must not
            // cost the NPC its region, schedule and map marker below.
            if (def.Appearance?.Distortion != null && def.Appearance.Distortion.Count > 0)
            {
                try
                {
                    var avatar = gameObject.GetComponentInChildren<Avatar>(true);
                    if (avatar != null)
                    {
                        API.ApplyDistortion(avatar, def);
                        Spawn.DistortionKeeper.Track(avatar, def);
                    }
                    else Core.Log?.Warning($"'{def.Id}': no avatar found - body distortion skipped.");
                }
                catch (Exception ex)
                {
                    Core.Log?.Error($"'{def.Id}': applying body distortion failed ({ex.Message}).");
                }
            }

            Personnel.Spawn.CustomLines.Apply(gameObject, def);

            if (!string.IsNullOrWhiteSpace(def.Spawn?.Region))
            {
                if (Parse.TryParseEnum(def.Spawn.Region, out S1API.Map.Region region)) Region = region;
                else Core.Log?.Warning($"'{def.Id}': unknown spawn.region '{def.Spawn.Region}' - ignored.");
            }

            // Behaviour stats are runtime state, not prefab config - applied here per instance.
            if (def.Behavior != null)
            {
                try
                {
                    Aggressiveness = def.Behavior.Aggression;
                    MaxHealth = def.Behavior.MaxHealth;
                    if (def.Behavior.Scale > 0f) Scale = def.Behavior.Scale;
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning($"'{def.Id}': applying behavior stats failed ({ex.Message}).");
                }
            }

            if (def.Schedule != null && def.Schedule.Count > 0)
            {
                try { Spawn.DoorlessStayGuard.Apply(gameObject, def.Id); }
                catch (Exception ex) { Core.Log?.Error($"'{def.Id}': checking stay-in-building doors failed ({ex.Message})."); }

                try { Schedule.Enable(); }
                catch (Exception ex) { Core.Log?.Warning($"'{def.Id}': Schedule.Enable failed ({ex.Message})."); }
            }

            // Phone-map marker (the relationship unlock happens at prefab-config time so the contact also
            // shows a real name). contact.mapMarker=false opts out.
            if (def.Contact?.MapMarker != false)
                API.AddMapMarker(gameObject);
        }
    }
}
