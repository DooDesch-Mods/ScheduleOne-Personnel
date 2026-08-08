# Personnel - Custom NPCs for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/personnel](https://support.doodesch.de/personnel).

> The NPC framework for Schedule I. Personnel lets anyone ship custom NPCs as simple pack folders -
> designed in [Personify](https://github.com/DooDesch-Mods/ScheduleOne-Personify) or written by hand -
> and lets mods spawn them as real, first-class S1API NPCs: networked, saved, and walking the world
> as if the mod author had built them in code. Built on [S1API](https://github.com/ifBars/S1API).

![Version](https://img.shields.io/badge/version-2.1.1-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)
![Type](https://img.shields.io/badge/type-library%2Fdependency-lightgrey)

Personnel is primarily a **library / dependency**. On its own it adds no NPCs - it's the thing NPC
packs and NPC-spawning mods depend on. Install it alongside an NPC pack and the NPCs appear:
since 2.0 a pack can opt into **auto-registration** and needs no mod code at all.

## Documentation

- 📖 **[Documentation](https://docs.doodesch.de/mods/personnel/)** - the full guide: pack
  format and id rules, the API (spawning real S1API NPCs), custom PNG layer authoring, multiplayer,
  troubleshooting.
- 🧩 **[Personify](https://github.com/DooDesch-Mods/ScheduleOne-Personify)** - the in-game editor that
  designs and exports these packs.

## Features

- **NPC mods without code.** A pack manifest can carry the whole NPC: spawn point and region, a
  daily schedule (walk routes, buildings, seats, vending machines, slot machines, dialogue spots,
  car trips), customer or dealer economy, inventory, relationships and contact presentation. Flip
  `"autoRegister": true` and Personnel spawns them as real world NPCs that run their day on their
  own.
- **Real S1API NPCs, not props.** Prefab, networking, save/load and mugshot are handled by S1API -
  the same machinery hand-coded NPC mods use.
- **Deep appearance.** The full avatar-settings surface: body, skin, hair, face, eyes, eyebrows,
  clothing layers, accessories, plus **custom PNG layers** (e.g. tattoos) loaded from the pack folder.
- **Physical or contact-only.** Most roster NPCs can stay phone contacts (near-zero cost); only the
  ones that should walk the world are physical. Big packs stay fast, also on Steam Deck.
- **NPC packs are plain folders** - a `manifest.json` plus optional PNGs under
  `UserData/Personnel/Packs/<PackName>/`. Design them live in-game with
  [Personify](https://github.com/DooDesch-Mods/ScheduleOne-Personify).
- **Coordinates from the console.** The game shows none, so `personnel pos 07:30` gives you a
  finished `walkTo` action for the spot you are standing on, `personnel spawn` a full spawn block,
  and `personnel route` collects a whole day's route. Everything lands on your clipboard.
- **Stable, save-safe ids** with rename escape hatches (`packId`, `saveId`).
- **Bundled example pack** (off by default) drops a working manifest template to copy.

## Requirements

| Component | Version / Source |
|-----------|------------------|
| Schedule I | IL2CPP (current Steam public build) |
| MelonLoader | `0.7.3+` |
| S1API | [ifBars/S1API_Forked](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/) |

## Installation

### Recommended: a Thunderstore mod manager
Install with r2modman / Gale from the Schedule I community; the dependencies (MelonLoader, S1API) are
pulled in automatically. Then install any NPC pack or NPC mod that lists Personnel as a dependency.

### Manual
1. Install **MelonLoader 0.7.3** for Schedule I.
2. Install **S1API** (its DLLs go in `Mods/` and `Plugins/` per its own instructions).
3. Drop **`Personnel.dll`** into your Schedule I `Mods/` folder.
4. Add NPC packs (see below), or enable the bundled example pack (see Configuration).

## For players: using NPC packs

An NPC pack is just a folder under:

```
<Schedule I>/UserData/Personnel/Packs/<PackName>/
```

containing a `manifest.json` and any PNGs it references. On startup Personnel logs each loaded pack
with the NPC ids it provides. Packs with `autoRegister` bring their NPCs into the world by
themselves; older appearance-only packs are used by whatever consumer mod spawns them. In co-op,
everyone needs the same packs installed - the same rule as for mods.

## For pack authors: the pack format

The comfortable way is the in-game editor **Personify** (a Side Hustle gamemode): design NPCs live on
the menu character, then hit Export - it writes a ready-to-publish pack. By hand, create
`UserData/Personnel/Packs/<YourPack>/manifest.json`:

```json
{
  "name": "My Pack",
  "author": "you",
  "schemaVersion": 2,
  "autoRegister": true,
  "npcs": [
    {
      "name": "Pale",
      "appearance": {
        "gender": 0.5, "height": 1.0, "weight": 0.4,
        "skinColor": "#8899AA", "hairPath": "", "hairColor": "#101014",
        "faceLayers": [ { "file": "grin.png", "tint": "#FFFFFF" } ]
      },
      "spawn": { "x": -66.4, "y": -2.9, "z": 86.1, "region": "Westville", "physical": true },
      "schedule": [
        { "type": "walkTo", "time": "07:30", "position": [-70.1, -2.9, 80.0] },
        { "type": "stayInBuilding", "time": "09:00", "duration": 240, "building": "Thrifty Threads" }
      ]
    }
  ]
}
```

- An authored `id` is respected (normalized); without one it is derived as `<packname>_<npcname>`.
  Ids are the save identity - never change them once a pack shipped (use `saveId` when renaming).
- Appearance mirrors the S1API avatar-settings surface; every field is optional and defaults to the
  game's baseline. Layers take either a `path` (existing game layer) or a `file` (pack-relative PNG).
- On top of `appearance` there are `spawn`, `schedule`, `customer`, `dealer`, `inventory`,
  `relationships` and `contact` blocks - the
  [Pack Format wiki page](https://docs.doodesch.de/mods/personnel/)
  documents every field.
- Enable the example pack (Configuration below) for a complete, working template.

## For mod developers: spawning the NPCs

Reference `Personnel.dll` (add `[assembly: MelonOptionalDependencies("Personnel")]`) and declare one
tiny subclass per NPC you want - then spawn it the normal S1API way:

```csharp
public sealed class PaleNpc : Personnel.PersonnelNpc
{
    protected override string DefId => "examples_pale";   // id from an installed pack
}

// wherever you populate the world:
var npc = new PaleNpc();          // S1API builds the prefab, networks and saves it
npc.Position = spawnPoint;        // then use S1API as usual: schedules, dialogue, ...
```

`DefId` must return a constant (S1API builds prefabs from uninitialized instances). Lower-level
entry points on `Personnel.API`: `All` / `TryGet(id, out def)` to enumerate definitions,
`BuildAvatarSettings(def)` / `ApplyAppearance(avatar, def)` to realise just the look on any avatar,
and `ConfigureFromDef(builder, def)` if you keep your own `NPC` subclass. A complete reference
consumer lives in the repo as `PersonOfInterest`.

## Configuration

Settings live in `UserData/MelonPreferences.cfg`
under `Personnel_01_Main`.

| Setting | Default | What it does |
|---|---|---|
| `LoadExamplePack` | `false` | When on, drops a small example pack into `UserData/Personnel/Packs/Examples` on startup (if not already there) - ready-made NPCs plus a manifest template to copy. Requires a game restart. Never overwrites an existing Examples folder. |
| `EnableAutoRegister` | `true` | Kill switch for pack auto-registration. When off, packs with `autoRegister` load as data only and nothing spawns. Requires a game restart. |

## How it works

Packs are parsed into plain definitions at startup (no Unity objects). When a consumer first uses a
definition, Personnel maps it onto S1API's avatar-settings builder; custom PNG layers are loaded into
textures, wrapped into cloned avatar layers via S1API's `AvatarLayerFactory`, and registered at custom
Resources paths - so the game's own pipeline renders, saves and networks them like built-in layers.
Full NPCs ride entirely on S1API's prefab/networking machinery via `PersonnelNpc`.

## Compatibility

- IL2CPP build only (current Steam public branch).
- **Multiplayer:** NPCs spawned via S1API follow S1API's networking. Custom PNG layers are referenced
  by resource-path string - clients need Personnel and the same pack to render them; others simply
  don't show that layer (no desync, no crash).

## Credits

- **DooDesch** - mod author.
- **[ifBars/S1API](https://github.com/ifBars/S1API)** - the modding API this is built on.

## License

Provided as-is under the [MIT License](LICENSE.md).
