using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Personnel.Content
{
    /// <summary>
    /// Deserialization shape for a pack's <c>manifest.json</c> (parsed with managed Newtonsoft.Json).
    /// Field names are intentionally lowercase to match the JSON authors write. Colours are "#RRGGBB[AA]".
    /// </summary>
    public sealed class NpcPackManifest
    {
        public string name;
        public string author;
        public List<NpcEntry> npcs;
    }

    /// <summary>One NPC entry inside a pack manifest.</summary>
    public sealed class NpcEntry
    {
        public string id;
        public string name;
        public AppearanceJson appearance;
        public BehaviorJson behavior;
        public SpawnJson spawn;

        /// <summary>Free-form per-consumer extension blocks (e.g. "backrooms"); kept raw and handed to consumers.</summary>
        public JObject extensions;
    }

    /// <summary>Mirror of the vanilla <c>AvatarSettings</c> knobs. All fields nullable so omitted keys keep defaults.</summary>
    public sealed class AppearanceJson
    {
        public float? gender;
        public float? height;
        public float? weight;
        public string skinColor;
        public string hairPath;
        public string hairColor;
        public float? eyebrowScale;
        public float? eyebrowThickness;
        public float? eyebrowRestingHeight;
        public float? eyebrowRestingAngle;
        public string leftEyeLidColor;
        public string rightEyeLidColor;
        public EyeJson leftEye;
        public EyeJson rightEye;
        public string eyeballMaterial;
        public string eyeBallTint;
        public float? pupilDilation;
        public List<LayerJson> faceLayers;
        public List<LayerJson> bodyLayers;
        public List<LayerJson> accessories;
        /// <summary>Extreme body distortion (Personify's Experimental tab), keyed by bone/mesh name. Omitted for plain NPCs.</summary>
        public Dictionary<string, BoneDistortionJson> distortion;
    }

    /// <summary>One bone's (or mesh's) distortion: non-uniform scale, or fully hidden.</summary>
    public sealed class BoneDistortionJson
    {
        public float? scaleX;
        public float? scaleY;
        public float? scaleZ;
        public bool? hide;
    }

    public sealed class EyeJson
    {
        public float? top;
        public float? bottom;
    }

    /// <summary>A layer: either an existing game <c>path</c> OR a pack-relative <c>file</c> (+ <c>kind</c> face|body).</summary>
    public sealed class LayerJson
    {
        public string path;
        public string file;
        /// <summary>For custom <c>file</c> layers: "face" or "body". Ignored for accessories.</summary>
        public string kind;
        public string tint;
        /// <summary>Accessory alias for <see cref="tint"/>.</summary>
        public string color;
    }

    public sealed class BehaviorJson
    {
        public float? aggression;
        public float? maxHealth;
        public float? scale;
        public string conversation;
    }

    public sealed class SpawnJson
    {
        public float? x;
        public float? y;
        public float? z;
        public string region;
    }
}
