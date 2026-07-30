# Changelog

All notable changes to Personnel are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [2.1.0] - 2026-07-30

Writing a schedule meant guessing coordinates, because the game shows none anywhere. Now the console
hands them to you.

### Added

- Dev-console commands for pack authors (console has to be enabled in Settings):
  - `personnel pos [HH:MM]` - the spot you are standing on, as `[x, y, z]` or as a finished `walkTo`
    action for that time.
  - `personnel spawn` - the same spot as a `spawn` block, with `rotationY` and `region` filled in.
  - `personnel route HH:MM` - collects a step per stop in `UserData/Personnel/route.json`;
    `personnel route show` prints the block ready to paste into `"schedule": [ ... ]`, `clear`
    starts over.
  - `personnel npcs [filter]` - loaded definitions, and where the physical ones are right now.
  - `personnel help` - the list.
- Results are copied to the clipboard and written to the MelonLoader log, with an in-game
  notification confirming the command ran.

## [2.0.0] - 2026-07-22

NPC mods without code: a pack manifest can now describe everything an NPC needs - where it spawns,
what it does all day, its economy role - and Personnel spawns it as a real world NPC on its own.

### Added

- `autoRegister` (pack level) / `spawn.auto` (per NPC): Personnel registers opted-in NPCs as real,
  networked, saved world NPCs with no consumer mod involved. A kill switch lives in the settings
  (`EnableAutoRegister`). Packs that already ship a consumer DLL are safe: a compiled NPC class for
  the same id always wins over the generated one.
- `spawn` grew `rotationY` (spawn yaw), `region` (now actually applied) and `physical`. Non-physical
  NPCs are phone contacts only - no world body, no pathing cost. Default: physical exactly when the
  NPC has a schedule.
- `schedule`: full daily schedules in the manifest - `walkTo`, `stayInBuilding`, `sit`,
  `useVendingMachine`, `useAtm`, `useSlotMachine`, `locationDialogue`, `locationAction`,
  `driveToCarPark`, `dealSignal`. Times are `"HH:MM"` strings.
- `relationships`: `delta`, `unlocked`, `unlockType`, `connections` (by NPC id).
- `customer`: the whole customer economy - spending, orders per week, preferred day/time, standards,
  direct approach, first sample, mutual relation requirement, call-police chance, dependence,
  affinities, preferred properties.
- `dealer`: type, cut, signing fee, home, completed-deals variable, quality tolerances. Definitions
  with a dealer block now get the proper dealer base prefab (previously manifest dealers were built
  on the civilian prefab).
- `inventory`: random cash range, startup items (with quantities), clear-each-night.
- `contact`: `mapMarker: false` removes the phone-map marker, `visible: false` (experimental) skips
  the contact unlock.
- `behavior.aggression`, `maxHealth` and `scale` are now actually applied to spawned NPCs.
- Id stability: an authored `id` in the manifest is respected (previously always derived from folder
  and name), `packId` pins the derivation prefix, and `saveId` lets a renamed NPC keep matching old
  saves. `schemaVersion` marks the manifest format (current: 2).
- Distant custom NPCs get a proper billboard impostor instead of a blank one (on S1API builds that
  support impostor configuration).

### Changed

- Pack and NPC registration order is now deterministic across machines - co-op peers agree on it.
- Contact unlock and map marker are unchanged by default but can be opted out per NPC.

## [1.1.0] - 2026-07-08

### Fixed

- Custom NPCs now show their real name in the phone Contacts app (previously "???") and can be found
  on the map. Personnel unlocks each spawned NPC's contact and gives it a map marker.
- Clothing no longer carries over from one spawned NPC to the next. The game clears only six of its
  eight avatar layer slots, so an NPC with more layers than that leaves a garment bound to the material
  for whoever is applied after it.
- A definition asking for more than eight body layers no longer loses one at random. The surplus is
  dropped deliberately, clothing first, and named in the log.

### Added

- Opt-in economy roles via a pack's `behavior.conversation`: `"customer"` or `"dealer"` makes the NPC
  a real customer or dealer, while the default `"none"` stays a plain, non-economy contact.
- `API.AddMapMarker(GameObject)` to add a map marker to any live custom NPC.

## [1.0.0] - 2026-07-06

Initial release.

### Added

- NPC pack loading from `UserData/Personnel/Packs/<PackName>/` (`manifest.json` + optional PNGs),
  with per-pack log lines listing the loaded NPC ids.
- Deep appearance definitions mirroring the S1API avatar-settings surface: body, skin, hair, face,
  eyes, eyebrows, clothing layers, accessories, and custom PNG layers loaded from the pack folder.
- Duplicate-proof NPC ids, always derived as `packname_npcname` (normalized); manifest `id` fields
  are ignored.
- `PersonnelNpc` base class: one tiny subclass per NPC turns a pack definition into a full S1API NPC
  (prefab, networking, save/load handled by S1API).
- Public API: `All`, `TryGet`, `Register`, `BuildAvatarSettings(def)`, `ApplyAppearance(avatar, def)`,
  `ConfigureFromDef(builder, def)`, `OnReloaded`, `Reload`.
- Bundled example pack (off by default, `LoadExamplePack`) with two NPCs as a copyable template.
- Mod Manager & Phone App settings integration.
