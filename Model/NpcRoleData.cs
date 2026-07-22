using System.Collections.Generic;

namespace Personnel.Model
{
    /// <summary>
    /// Phone-contact presentation of an NPC. Both default to the classic Personnel behaviour (visible contact
    /// with a map marker) so existing packs are unaffected.
    /// </summary>
    public sealed class NpcContact
    {
        /// <summary>Unlock the relationship so Contacts shows the real name (instead of "???"). Default true.</summary>
        public bool? Visible;

        /// <summary>Add the phone-map marker (NPCPoI). Default true.</summary>
        public bool? MapMarker;
    }

    /// <summary>Relationship defaults applied at prefab-config time (S1API WithRelationshipDefaults).</summary>
    public sealed class NpcRelationships
    {
        /// <summary>Relationship delta 0..5.</summary>
        public float? Delta;

        /// <summary>Explicit unlock override; falls back to contact.visible, then true.</summary>
        public bool? Unlocked;

        /// <summary>"Recommendation" or "DirectApproach".</summary>
        public string UnlockType;

        /// <summary>Connected NPC ids (pack-prefixed def ids, e.g. "mypack_kyle_boone").</summary>
        public List<string> Connections;
    }

    /// <summary>An inclusive float range (e.g. weekly spending).</summary>
    public sealed class NpcRange
    {
        public float Min;
        public float Max;
    }

    /// <summary>Customer economy defaults (S1API WithCustomerDefaults). Only set fields are applied.</summary>
    public sealed class NpcCustomer
    {
        public NpcRange Spending;
        public NpcRange OrdersPerWeek;
        public string PreferredOrderDay;
        /// <summary>hhmm int (parsed from "HH:MM").</summary>
        public int? OrderTime;
        public string Standards;
        public bool? AllowDirectApproach;
        public bool? GuaranteeFirstSample;
        public NpcRange MutualRelationRequirement;
        public float? CallPoliceChance;
        public float? DependenceBase;
        public float? DependenceMultiplier;
        /// <summary>Drug type name -> affinity (-1..1).</summary>
        public Dictionary<string, float> Affinities;
        /// <summary>Preferred product property ids.</summary>
        public List<string> PreferredProperties;
    }

    /// <summary>Dealer economy defaults (S1API WithDealerDefaults). Only set fields are applied.</summary>
    public sealed class NpcDealer
    {
        /// <summary>"player" or "cartel" (S1API DealerType).</summary>
        public string Type;
        public float? Cut;
        public float? SigningFee;
        public string Home;
        public string CompletedDealsVariable;
        public bool? AllowInsufficientQuality;
        public bool? AllowExcessQuality;
    }

    /// <summary>One startup inventory item (id + count).</summary>
    public sealed class NpcInventoryItem
    {
        public string Id;
        public int Quantity = 1;
    }

    /// <summary>Inventory defaults (S1API WithInventoryDefaults).</summary>
    public sealed class NpcInventory
    {
        public NpcRange Cash;
        public List<NpcInventoryItem> Items;
        public bool? ClearEachNight;
    }

    /// <summary>
    /// One daily-schedule action, kept close to its manifest shape (strings/nullables). Converted into the
    /// matching S1API IScheduleActionSpec by <c>ScheduleSpecFactory</c>, which owns validation and logging.
    /// </summary>
    public sealed class NpcScheduleAction
    {
        /// <summary>Discriminator: walkTo, stayInBuilding, sit, useVendingMachine, useAtm, useSlotMachine,
        /// locationDialogue, locationAction, driveToCarPark, dealSignal, handleDeal.</summary>
        public string Type;

        /// <summary>Start time, hhmm int (parsed from "HH:MM"). -1 = not set.</summary>
        public int Time = -1;

        /// <summary>Optional stable action name (shown in logs/debug).</summary>
        public string Name;

        public UnityEngine.Vector3? Position;
        public bool? FaceDestination;
        public float? Within;
        public bool? WarpIfSkipped;
        public int? DurationMinutes;

        public string Building;
        public int? DoorIndex;

        public string SeatSet;
        public string SeatSetPath;
        public bool? IncludeInactive;

        public string MachineGuid;
        public string AtmGuid;

        public int? Bet;
        public string Mode;
        /// <summary>End time for untilTime slot sessions, hhmm int. -1 = not set.</summary>
        public int EndTime = -1;
        public int? Spins;
        public float? TimeBetweenSpins;
        public float? MaxSearchDistance;

        public int? GreetingOverride;
        public int? Choice;

        /// <summary>locationAction behaviour: none, smokeBreak, graffiti, drinking, holdItem.</summary>
        public string Action;
        public string EquippablePath;
        public string GraffitiRegion;

        public string ParkingLot;
        public string Vehicle;
        public string CreateVehicleCode;
        public UnityEngine.Vector3? CreateVehiclePosition;
        public float? CreateVehicleRotationY;
    }
}
