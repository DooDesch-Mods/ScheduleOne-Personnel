using System;
using System.Collections.Generic;
using Personnel.Model;
using Personnel.Util;
using S1API.Casino;
using S1API.Entities.Schedule;
using UnityEngine;

namespace Personnel.Spawn
{
    /// <summary>
    /// Turns a definition's manifest-authored schedule into the matching S1API schedule specs. Owns all
    /// per-action validation: a broken action is warned about and skipped, the rest of the schedule survives.
    /// Times are hhmm ints (already parsed by the pack loader).
    /// </summary>
    internal static class ScheduleSpecFactory
    {
        public static List<IScheduleActionSpec> Build(NpcDef def)
        {
            var specs = new List<IScheduleActionSpec>();
            if (def?.Schedule == null) return specs;

            foreach (NpcScheduleAction a in def.Schedule)
            {
                if (a == null) continue;
                try
                {
                    IScheduleActionSpec spec = BuildOne(def, a);
                    if (spec != null) specs.Add(spec);
                }
                catch (Exception ex)
                {
                    Warn(def, a, $"failed to build ({ex.Message})");
                }
            }
            return specs;
        }

        private static IScheduleActionSpec BuildOne(NpcDef def, NpcScheduleAction a)
        {
            switch (Fold(a.Type))
            {
                case "walkto":
                {
                    if (a.Position == null) return Fail(def, a, "needs 'position'");
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    var s = new WalkToSpec { Destination = a.Position.Value, StartTime = a.Time, Name = a.Name };
                    if (a.FaceDestination.HasValue) s.FaceDestinationDirection = a.FaceDestination.Value;
                    if (a.Within.HasValue) s.Within = a.Within.Value;
                    if (a.WarpIfSkipped.HasValue) s.WarpIfSkipped = a.WarpIfSkipped.Value;
                    return s;
                }
                case "stayinbuilding":
                {
                    if (string.IsNullOrWhiteSpace(a.Building)) return Fail(def, a, "needs 'building'");
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    var s = new StayInBuildingSpec { BuildingName = a.Building, StartTime = a.Time, Name = a.Name };
                    if (a.DurationMinutes.HasValue) s.DurationMinutes = a.DurationMinutes.Value;
                    if (a.DoorIndex.HasValue) s.DoorIndex = a.DoorIndex.Value;
                    return s;
                }
                case "sit":
                {
                    if (string.IsNullOrWhiteSpace(a.SeatSet) && string.IsNullOrWhiteSpace(a.SeatSetPath))
                        return Fail(def, a, "needs 'seatSet' or 'seatSetPath'");
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    var s = new SitSpec { StartTime = a.Time, Name = a.Name };
                    if (!string.IsNullOrWhiteSpace(a.SeatSet)) s.SeatSetName = a.SeatSet;
                    if (!string.IsNullOrWhiteSpace(a.SeatSetPath)) s.SeatSetPath = a.SeatSetPath;
                    if (a.DurationMinutes.HasValue) s.DurationMinutes = a.DurationMinutes.Value;
                    if (a.WarpIfSkipped.HasValue) s.WarpIfSkipped = a.WarpIfSkipped.Value;
                    if (a.IncludeInactive.HasValue) s.IncludeInactiveSearch = a.IncludeInactive.Value;
                    return s;
                }
                case "usevendingmachine":
                {
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    return new UseVendingMachineSpec { StartTime = a.Time, MachineGUID = a.MachineGuid, Name = a.Name };
                }
                case "useatm":
                {
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    return new UseATMSpec { StartTime = a.Time, ATMGUID = a.AtmGuid, Name = a.Name };
                }
                case "useslotmachine":
                {
                    if (a.Position == null) return Fail(def, a, "needs 'position'");
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    var s = new UseSlotMachineSpec { MachinePosition = a.Position.Value, StartTime = a.Time, Name = a.Name };
                    if (a.Bet.HasValue) s.BetAmount = a.Bet.Value;
                    if (a.Spins.HasValue) s.SpinCount = a.Spins.Value;
                    if (a.EndTime >= 0) s.EndTime = a.EndTime;
                    if (a.TimeBetweenSpins.HasValue) s.TimeBetweenSpins = a.TimeBetweenSpins.Value;
                    if (a.MaxSearchDistance.HasValue) s.MaxSearchDistance = a.MaxSearchDistance.Value;
                    if (!string.IsNullOrWhiteSpace(a.Mode))
                    {
                        // "single" is friendlier than the enum's "SingleSpin"; both fold to a match via alias.
                        string mode = Fold(a.Mode) == "single" ? "SingleSpin" : a.Mode;
                        if (Parse.TryParseEnum(mode, out GamblingSessionMode m)) s.SessionMode = m;
                        else Warn(def, a, $"unknown mode '{a.Mode}' - using {s.SessionMode}");
                    }
                    return s;
                }
                case "locationdialogue":
                {
                    if (a.Position == null) return Fail(def, a, "needs 'position'");
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    var s = new LocationDialogueSpec { Destination = a.Position.Value, StartTime = a.Time, Name = a.Name };
                    if (a.FaceDestination.HasValue) s.FaceDestinationDirection = a.FaceDestination.Value;
                    if (a.Within.HasValue) s.Within = a.Within.Value;
                    if (a.WarpIfSkipped.HasValue) s.WarpIfSkipped = a.WarpIfSkipped.Value;
                    if (a.GreetingOverride.HasValue) s.GreetingOverrideToEnable = a.GreetingOverride.Value;
                    if (a.Choice.HasValue) s.ChoiceToEnable = a.Choice.Value;
                    return s;
                }
                case "locationaction":
                {
                    if (a.Position == null) return Fail(def, a, "needs 'position'");
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    var s = new LocationBasedActionSpec { Destination = a.Position.Value, StartTime = a.Time, Name = a.Name };
                    if (a.DurationMinutes.HasValue) s.DurationMinutes = a.DurationMinutes.Value;
                    if (a.FaceDestination.HasValue) s.FaceDestinationDirection = a.FaceDestination.Value;
                    if (a.Within.HasValue) s.Within = a.Within.Value;
                    if (a.WarpIfSkipped.HasValue) s.WarpIfSkipped = a.WarpIfSkipped.Value;
                    if (!string.IsNullOrWhiteSpace(a.Action))
                    {
                        if (Parse.TryParseEnum(a.Action, out LocationArriveBehaviour b)) s.ArriveBehaviour = b;
                        else Warn(def, a, $"unknown action '{a.Action}' - using None");
                    }
                    if (!string.IsNullOrWhiteSpace(a.EquippablePath))
                    {
                        if (s.ArriveBehaviour == LocationArriveBehaviour.Drinking) s.DrinkEquippablePath = a.EquippablePath;
                        else s.EquippableAssetPath = a.EquippablePath;
                    }
                    if (!string.IsNullOrWhiteSpace(a.GraffitiRegion))
                    {
                        if (Parse.TryParseEnum(a.GraffitiRegion, out S1API.Map.Region r)) s.GraffitiRegion = r;
                        else Warn(def, a, $"unknown graffitiRegion '{a.GraffitiRegion}' - ignored");
                    }
                    return s;
                }
                case "drivetocarpark":
                {
                    if (string.IsNullOrWhiteSpace(a.ParkingLot)) return Fail(def, a, "needs 'parkingLot'");
                    if (a.Time < 0) return Fail(def, a, "needs 'time'");
                    var s = new DriveToCarParkSpec { ParkingLotName = a.ParkingLot, StartTime = a.Time, Name = a.Name };
                    if (!string.IsNullOrWhiteSpace(a.Vehicle)) s.VehicleName = a.Vehicle;
                    if (!string.IsNullOrWhiteSpace(a.CreateVehicleCode))
                    {
                        s.VehicleCode = a.CreateVehicleCode;
                        if (a.CreateVehiclePosition.HasValue) s.VehicleSpawnPosition = a.CreateVehiclePosition.Value;
                        if (a.CreateVehicleRotationY.HasValue)
                            s.VehicleSpawnRotation = Quaternion.Euler(0f, a.CreateVehicleRotationY.Value, 0f);
                    }
                    if (string.IsNullOrWhiteSpace(a.Vehicle) && string.IsNullOrWhiteSpace(a.CreateVehicleCode))
                        return Fail(def, a, "needs 'vehicle' or 'createVehicle'");
                    return s;
                }
                case "dealsignal":
                    return new EnsureDealSignalSpec();
                case "handledeal":
                    // Obsolete in S1API since game 0.4.2f4 - deal handling is automatic (DealerAttendDealBehaviour).
                    Core.Log?.Msg($"'{def.Id}': schedule action 'handleDeal' is obsolete (deals are automatic) - ignored.");
                    return null;
                default:
                    return Fail(def, a, "unknown action type");
            }
        }

        private static IScheduleActionSpec Fail(NpcDef def, NpcScheduleAction a, string reason)
        {
            Warn(def, a, reason);
            return null;
        }

        private static void Warn(NpcDef def, NpcScheduleAction a, string message)
            => Core.Log?.Warning($"'{def.Id}' schedule action '{a.Type}': {message} - skipped/ignored.");

        private static string Fold(string s)
        {
            if (s == null) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }
    }
}
