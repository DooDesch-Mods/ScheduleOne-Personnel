# Personnel - Custom NPCs for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/personnel](https://support.doodesch.de/personnel).

> **The NPC framework for Schedule I.** NPC packs are plain folders - spawn points, daily schedules,
> customer/dealer economy, relationships, contacts - and Personnel spawns them as real S1API NPCs:
> networked, saved, walking their routines. Since 2.0, no mod code needed at all.

![Version](https://img.shields.io/badge/version-2.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![S1API](https://img.shields.io/badge/S1API-required-orange)

## What it does

- **NPC mods without code.** A pack's `manifest.json` can carry everything: spawn point and region,
  a daily schedule (walk routes, buildings, seats, vending machines, slot machines, dialogue spots,
  car trips), customer or dealer economy, inventory, relationships and contact presentation. Set
  `"autoRegister": true` and Personnel spawns the NPCs as real world NPCs on its own.
- **Real S1API NPCs, not props** - prefab, networking, save/load and mugshot handled by S1API, the
  same machinery hand-coded NPC mods use.
- **Physical or contact-only.** Most roster NPCs can stay phone contacts (near-zero cost); only the
  ones that should walk the world are physical. Big packs stay fast, also on Steam Deck.
- Deep appearance: body, skin, hair, face, eyes, eyebrows, clothing, accessories and custom PNG
  layers (e.g. tattoos). Design packs live in-game with **Personify**.
- Stable, save-safe NPC ids with rename escape hatches (`packId`, `saveId`).

## Requirements

- **Schedule I** (IL2CPP) with **MelonLoader 0.7.3+**.
- **S1API** (pulled in as a dependency).

## Using it

Install Personnel plus any NPC pack or NPC mod that depends on it. Packs live in
`UserData/Personnel/Packs/<PackName>/`; on startup the log lists each pack with its NPC ids and how
many were auto-registered. In co-op, everyone needs the same packs installed - the same rule as for
mods.

Want a template? Set **`LoadExamplePack`** to `true` (`UserData/MelonPreferences.cfg` under `Personnel_01_Main`) and restart - a small example pack is
dropped into `Packs/Examples`, including an NPC with a spawn point and daily schedule to copy.

## For pack authors and developers

Every manifest block (spawn, schedule, customer, dealer, inventory, relationships, contact) is
documented on the
[Pack Format wiki page](https://github.com/DooDesch-Mods/ScheduleOne-Personnel/wiki/Pack-Format).
Mods can still bring pack NPCs in with one tiny subclass:

```csharp
public sealed class PaleNpc : Personnel.PersonnelNpc
{
    protected override string DefId => "examples_pale";
}
var npc = new PaleNpc();   // spawns a fully networked, saved S1API NPC from the pack definition
```

Full pack format, API reference and examples on
[GitHub](https://github.com/DooDesch-Mods/ScheduleOne-Personnel).

## Settings

- `LoadExamplePack` (default `false`) - drop the bundled example pack on disk as a template.
- `EnableAutoRegister` (default `true`) - kill switch for pack auto-registration.

Editable in `UserData/MelonPreferences.cfg`.

## License

MIT. See the included LICENSE.md.
