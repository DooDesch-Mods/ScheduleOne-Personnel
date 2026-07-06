using System.Collections.Generic;
using UnityEngine;

namespace Personnel.Model
{
    /// <summary>
    /// A single custom NPC definition, resolved either from a pack manifest entry or the public API. Holds only
    /// managed data (appearance knobs + layer sources) until it is realised into a vanilla
    /// <c>AvatarSettings</c> on demand (see <see cref="Appearance.AvatarSettingsFactory"/>).
    /// </summary>
    public sealed class NpcDef
    {
        /// <summary>Pack-scoped unique id.</summary>
        public string Id;

        /// <summary>Display name (falls back to <see cref="Id"/>).</summary>
        public string DisplayName;

        /// <summary>Originating pack name (or "API"); used for logging and the stable unique key.</summary>
        public string Source;

        /// <summary>Absolute pack folder, used to resolve custom-layer PNGs. Null for API-registered defs.</summary>
        public string PackDir;

        /// <summary>The visual definition. Never null (defaults applied on load).</summary>
        public NpcAppearance Appearance = new NpcAppearance();

        /// <summary>Optional S1API-expressible behaviour. May be null.</summary>
        public NpcBehavior Behavior;

        /// <summary>Optional spawn hints for full S1API spawns. May be null.</summary>
        public NpcSpawn Spawn;

        /// <summary>
        /// Raw per-consumer extension blocks, keyed by name (e.g. "backrooms"), each the block's minified JSON.
        /// Consumers parse their own block with their own schema; unknown blocks are ignored. Never null.
        /// </summary>
        public IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>();

        /// <summary>Stable global key: "&lt;Source&gt;/&lt;Id&gt;". Used for de-duplication.</summary>
        public string Key => (Source ?? "?") + "/" + (Id ?? "?");
    }

    /// <summary>
    /// The full S1API-expressible avatar look. Field defaults mirror the game's own defaults
    /// (see <c>NPCAppearance.ApplyDefaultSettings</c>) so an empty definition still yields a valid NPC.
    /// </summary>
    public sealed class NpcAppearance
    {
        public float Gender = 0f;
        public float Height = 0.98f;
        public float Weight = 0.4f;
        public Color SkinColor = new Color32(150, 120, 95, 255);
        public string HairPath = "";
        public Color HairColor = Color.black;
        public float EyebrowScale = 1f;
        public float EyebrowThickness = 1f;
        public float EyebrowRestingHeight = 0f;
        public float EyebrowRestingAngle = 0f;
        public Color LeftEyeLidColor = new Color32(150, 120, 95, 255);
        public Color RightEyeLidColor = new Color32(150, 120, 95, 255);
        public float LeftEyeTop = 0.5f, LeftEyeBottom = 0.5f;
        public float RightEyeTop = 0.5f, RightEyeBottom = 0.5f;
        public string EyeballMaterial = "Default";
        public Color EyeBallTint = Color.white;
        public float PupilDilation = 1f;

        public List<NpcLayer> FaceLayers = new List<NpcLayer>();
        public List<NpcLayer> BodyLayers = new List<NpcLayer>();
        public List<NpcLayer> Accessories = new List<NpcLayer>();
    }

    /// <summary>
    /// One avatar layer. Exactly one source is used, in priority order: a preloaded <see cref="Texture"/>
    /// (API callers), a pack-relative PNG <see cref="File"/> (registered as a cloned avatar layer), or an existing
    /// game layer <see cref="Path"/> (referenced directly). <see cref="Tint"/> colours it.
    /// </summary>
    public sealed class NpcLayer
    {
        /// <summary>Existing in-game layer Resources path (e.g. "Avatar/Layers/Face/...").</summary>
        public string Path;

        /// <summary>Pack-relative PNG filename for a custom layer (registered lazily via S1API AvatarLayerFactory).</summary>
        public string File;

        /// <summary>Preloaded texture supplied by an API caller instead of a file.</summary>
        public Texture2D Texture;

        /// <summary>Layer tint / colour.</summary>
        public Color Tint = Color.white;
    }

    /// <summary>S1API-expressible behaviour/stat defaults (intentionally small).</summary>
    public sealed class NpcBehavior
    {
        public float Aggression = 0f;
        public float MaxHealth = 100f;
        public float Scale = 1f;
        /// <summary>"none" | "customer" | "dealer".</summary>
        public string Conversation = "none";
    }

    /// <summary>Spawn hints for full S1API spawns (secondary path).</summary>
    public sealed class NpcSpawn
    {
        public Vector3? Position;
        public string Region = "";
    }
}
