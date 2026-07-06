# Changelog

All notable changes to Personnel are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

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
