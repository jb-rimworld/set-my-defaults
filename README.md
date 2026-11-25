# Set my Defaults – Bills, Shelves, Zones, Policies, Colonists

Source: https://github.com/jb-rimworld/set-my-defaults (MIT, see `LICENSE`). Steam Workshop: 3737034968. packageId
`juliusberger.workbenchbilldefaults` (folder and assembly keep the old name `BetterWorkbenchDefaults`, so no player loses settings).

Set something up once, save it as a default, and it is applied from then on:

- **Workbenches**: a new (or reinstalled, still empty) bench gets its default bills, including custom names, Ideology styles and
  "drop in / count in" stockpiles (found again by name). Related buildings share defaults (the fueled stove uses the electric
  stove's bills). Applying works on every selected building of the same kind.
- **Shelves and storage**: new ones get their default filter and priority. Filters remember whole categories, so items a mod adds
  later are included.
- **Zones**: new stockpile, dumping and growing zones get their default filter or plant; hydroponics and other plant growers too.
- **Colonists**: every colonist who joins (start, recruit, wanderer, quest, birth) gets the default preset once (schedule, work
  priorities, outfit/drug/food/reading policy, hostility response, medical care). Colonists you already have are never changed,
  also after a caravan, transport pods or a cryptosleep casket.
- **Policies and colony settings**: a saved policy set and the colony settings (medical care per group, auto home area, auto
  rebuild, manual priorities) for new games.
- **More than 15 bills** per workbench without another mod (setting, live, no restart).

Everything lives in one **library** outside the saves (`<RimWorld user folder>/SetMyDefaults/Library.xml`), so it follows the player
into every colony. The Defaults button opens it in game (preview, rename, delete, default), **profiles** keep a whole setup under a
name, **share strings** copy a profile as text. A new colony opens JB Framework's **welcome card** with this mod's part: start with
"my defaults" or a profile, and choose what happens by itself. Presets of the 1.x versions are imported once; the old files are only
read. Two games open at once, a damaged library file or one of a newer version never cost a preset (lock + hash compare, unknown
data kept, the backup is used read-only and can be restored).

## Requirements and compatibility
- RimWorld **1.7**, Harmony, **JB Framework**. Built and tested on RimWorld 1.7. The 1.6 build is best effort and untested
  (1.7 is the focus; 1.6 gets the same 2.0 code, compiled against the 1.6 reference assemblies and API-scanned, never run).
- Compatibility page in the settings (each integration on while the other mod is present, switchable): Nice Bill Tab (refresh
  after applying; its 1.6 build on 1.7 counts), Project RimFactory Revived, Blueprints Forked, Free Will / Free-ish Will,
  Better Workbench Management (tested side by side), No Max Bills Redux (owns the bill limit while its patches work; when they did
  not apply, e.g. its 1.6 build on 1.7, this mod lifts the limit itself), Combat Extended (loadouts and ammo are never touched),
  Ideology (bill styles), Anomaly (medical defaults for entities and ghouls). Multiplayer: not supported, untested.

## Saves and removal
- Per save, the mod keeps only notes (which colonists already got their defaults, queued blueprint configs, the welcome card's
  state) inside JB Framework's game and map components; no class of this mod is written into a save. Saves of the previous
  versions still load: their old components (`PawnDefaultsGameComponent`, `WBD_PendingConfigs`) are read once
  (`KitRegistry.LegacyComponent`) and never written again.
- Removing the mod needs no preparation (there is no building, hediff, gene or other thing of this mod in a save). Bills, filters,
  zones and colonist settings it applied are plain RimWorld settings and stay. With JB Framework still installed the save loads
  without an error; the mod's notes wait in the framework's part of the save for its return.

## Layout and build
- `Common/` (languages, textures) + `1.7/Assemblies` for 1.7, `Common/` + `1.6/Assemblies` for 1.6 (same source, net472, one
  `#if RW16` shim in `Lib/LibraryFile.cs`; the frozen 1.x build of 24.11.2026 is in git, commit `b94b4e0`). `LoadFolders.xml` picks per game version.
- Build, tests, release check: `JBFramework/tools/jbmod BetterWorkbenchDefaults build|test all|saveload|removal|addition|release --dry`.
  Library proof without the game: `cd Source/Tests/SmdLibTests && dotnet run -c Release`.
- Code map: `WBD_Mod.cs` (registrations, hooks), `SmdRoster.cs` (roster, per-map data), `SmdGameData.cs` (per-save data),
  `Library/` + `Lib/` (library store and the Verse-free file layer), `UI/` (library pages, window, welcome card part), `Suites/`
  (in-game suites, compiled only with `JB_TESTS`).
