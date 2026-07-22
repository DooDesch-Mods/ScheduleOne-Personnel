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

        /// <summary>Manifest schema version this pack was authored against. Current: 2. Omitted = 1.</summary>
        public int? schemaVersion;

        /// <summary>Stable pack identity used to derive NPC ids. Defaults to the pack folder name, so setting
        /// it makes derived ids survive a folder rename. Never change it once a pack shipped.</summary>
        public string packId;

        /// <summary>Pack-level default for per-NPC <c>spawn.auto</c>: when true, every NPC in this pack is
        /// auto-registered as a world NPC (no consumer mod code needed) unless it opts out.</summary>
        public bool? autoRegister;

        public List<NpcEntry> npcs;
    }

    /// <summary>One NPC entry inside a pack manifest.</summary>
    public sealed class NpcEntry
    {
        public string id;
        public string name;

        /// <summary>Save-identity override: the id S1API persists. Set this to the OLD id after renaming an
        /// NPC (or its pack) so existing saves keep matching. Defaults to the NPC's id.</summary>
        public string saveId;

        public AppearanceJson appearance;
        public BehaviorJson behavior;
        public SpawnJson spawn;
        public ContactJson contact;
        public RelationshipsJson relationships;
        public CustomerJson customer;
        public DealerJson dealer;
        public InventoryJson inventory;
        public List<ScheduleActionJson> schedule;

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
        /// <summary>Spawn yaw in degrees.</summary>
        public float? rotationY;
        public string region;
        /// <summary>Physical world NPC (moves, collides, runs its schedule) vs. phone-contact only.
        /// Default: false, unless the NPC has a schedule.</summary>
        public bool? physical;
        /// <summary>Auto-register as a world NPC without any consumer mod code. Overrides the pack-level
        /// <c>autoRegister</c> default.</summary>
        public bool? auto;
    }

    /// <summary>Phone-contact presentation. Both default to true (classic Personnel behaviour).</summary>
    public sealed class ContactJson
    {
        public bool? visible;
        public bool? mapMarker;
    }

    public sealed class RelationshipsJson
    {
        public float? delta;
        public bool? unlocked;
        /// <summary>"Recommendation" | "DirectApproach".</summary>
        public string unlockType;
        /// <summary>Connected NPC def ids (pack-prefixed, e.g. "mypack_kyle_boone").</summary>
        public List<string> connections;
    }

    public sealed class MinMaxJson
    {
        public float? min;
        public float? max;
    }

    public sealed class CustomerJson
    {
        public MinMaxJson spending;
        public MinMaxJson ordersPerWeek;
        /// <summary>"Monday".."Sunday".</summary>
        public string preferredOrderDay;
        /// <summary>"HH:MM" or hhmm.</summary>
        public string orderTime;
        /// <summary>"VeryLow" | "Low" | "Moderate" | "High" | "VeryHigh".</summary>
        public string standards;
        public bool? allowDirectApproach;
        public bool? guaranteeFirstSample;
        /// <summary>min = required relation at 50% spend, max = at 100%.</summary>
        public MinMaxJson mutualRelationRequirement;
        public float? callPoliceChance;
        public DependenceJson dependence;
        /// <summary>Drug type name -> affinity (-1..1), e.g. { "marijuana": 0.6 }.</summary>
        public Dictionary<string, float> affinities;
        /// <summary>Preferred product property ids.</summary>
        public List<string> preferredProperties;
    }

    public sealed class DependenceJson
    {
        public float? @base;
        public float? multiplier;
    }

    public sealed class DealerJson
    {
        /// <summary>"player" | "cartel".</summary>
        public string type;
        public float? cut;
        public float? signingFee;
        /// <summary>Home building name.</summary>
        public string home;
        public string completedDealsVariable;
        public bool? allowInsufficientQuality;
        public bool? allowExcessQuality;
    }

    public sealed class InventoryJson
    {
        public MinMaxJson cash;
        /// <summary>Item entries; each either a plain string id or { "id": "...", "quantity": 2 }.</summary>
        public List<JToken> items;
        public bool? clearEachNight;
    }

    /// <summary>
    /// One schedule action; <c>type</c> picks which of the optional fields apply. Times are "HH:MM" strings
    /// (raw hhmm numbers are also accepted). Positions are [x,y,z] arrays.
    /// </summary>
    public sealed class ScheduleActionJson
    {
        /// <summary>walkTo | stayInBuilding | sit | useVendingMachine | useAtm | useSlotMachine |
        /// locationDialogue | locationAction | driveToCarPark | dealSignal | handleDeal.</summary>
        public string type;
        public string time;
        public string name;

        public float[] position;
        public bool? faceDestination;
        public float? within;
        public bool? warpIfSkipped;
        /// <summary>Duration in in-game minutes (stayInBuilding, sit, locationAction).</summary>
        public int? duration;

        public string building;
        public int? doorIndex;

        /// <summary>Seat set object name (sit).</summary>
        public string seatSet;
        /// <summary>Seat set full scene path (sit) - alternative to <c>seatSet</c>.</summary>
        public string seatSetPath;
        public bool? includeInactive;

        public string machineGuid;
        public string atmGuid;

        public int? bet;
        /// <summary>single | spinCount | untilTime | untilBroke | untilTimeOrBroke (useSlotMachine).</summary>
        public string mode;
        public string endTime;
        public int? spins;
        public float? timeBetweenSpins;
        public float? maxSearchDistance;

        public int? greetingOverride;
        public int? choice;

        /// <summary>none | smokeBreak | graffiti | drinking | holdItem (locationAction).</summary>
        public string action;
        public string equippablePath;
        public string graffitiRegion;

        /// <summary>Parking lot name (driveToCarPark).</summary>
        public string parkingLot;
        /// <summary>Existing vehicle name (driveToCarPark).</summary>
        public string vehicle;
        public CreateVehicleJson createVehicle;
    }

    /// <summary>Spawn a vehicle for driveToCarPark instead of referencing an existing one.</summary>
    public sealed class CreateVehicleJson
    {
        /// <summary>Vehicle code, e.g. "shitbox".</summary>
        public string code;
        public float[] position;
        public float? rotationY;
    }
}
