# Personnel - Custom NPCs for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de](https://support.doodesch.de).

> **The NPC framework for Schedule I.** NPC packs are plain folders (designed in-game with Personify
> or written by hand); mods spawn them as real S1API NPCs - networked, saved, walking the world as if
> hand-coded. On its own this mod adds no NPCs; it's the dependency NPC packs and NPC mods are built on.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)

## What it does

- Loads NPC packs from `UserData/Personnel/Packs/<PackName>/` - a `manifest.json` plus optional PNGs
  (custom layers like tattoos). No code required to make one: use the in-game editor **Personify**.
- Lets mods spawn any pack NPC as a **full S1API NPC** with one tiny subclass - prefab, networking and
  save/load handled by S1API - or apply just the designed look to any avatar via the API.
- Deep appearance: body, skin, hair, face, eyes, eyebrows, clothing, accessories and custom PNG layers.
- Duplicate-proof NPC ids, derived as `packname_npcname`.

**Early release:** the core paths are verified in-game, but 1.0.0 has not yet seen extended real-world
testing - it will mature over the coming releases. Reports at
[support.doodesch.de](https://support.doodesch.de) directly shape the next version.

## Requirements

- **Schedule I** (IL2CPP) with **MelonLoader 0.7.3+**.
- **S1API** (pulled in as a dependency).
- Optional: **Mod Manager & Phone App** for the in-game settings UI.

## Using it

Install Personnel plus any NPC pack or NPC mod that depends on it. Packs live in
`UserData/Personnel/Packs/<PackName>/`; on startup the log lists each pack with its NPC ids.

Want a template? Set **`LoadExamplePack`** to `true` (Mod Manager & Phone App UI or
`UserData/MelonPreferences.cfg` under `Personnel_01_Main`) and restart - a small example pack with two
NPCs is dropped into `Packs/Examples` to copy.

## For developers

```csharp
public sealed class PaleNpc : Personnel.PersonnelNpc
{
    protected override string DefId => "examples_pale";
}
var npc = new PaleNpc();   // spawns a fully networked, saved S1API NPC from the pack definition
```

Full pack format, API reference and a working example consumer on
[GitHub](https://github.com/DooDesch-Mods/ScheduleOne-Personnel).

## Settings

`LoadExamplePack` (default `false`) - drop the bundled example pack on disk as a template. Editable in
the Mod Manager & Phone App UI or `UserData/MelonPreferences.cfg`.

## License

MIT. See the included LICENSE.md.
