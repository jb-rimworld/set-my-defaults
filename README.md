# Set my Defaults (and more)

A lightweight RimWorld 1.6 mod that gives your **colonists** and your **buildings** remembered
defaults — so new ones start configured the way you like, automatically.

> The internal assembly/folder name is kept as `BetterWorkbenchDefaults` for continuity with the
> original "Workbench Defaults" release; only the in-game name changed.

## Pawn defaults

Save a colonist's configuration as a named **pawn preset**:

- **Schedule** (the 24-hour timetable)
- **Work priorities**
- **Apparel / outfit policy**, **drug policy**, **food policy**, **reading policy**
- **Hostility response** and **medical care + self-tend**

One preset is the **default**, applied **once** to every genuinely-new colonist — starting pawns,
recruits, joiners, and babies (work priorities kick in once a child is old enough to work). Each
axis can be toggled on/off independently in mod settings.

Select a colonist and use the **"Defaults" command button** to:

- **Save this colonist's setup as a preset** (the first one becomes the default).
- **Apply a preset** to the selected colonist — or to **all** selected colonists at once.
- **Set the default preset** / **Manage presets** (rename, delete, choose default).

Presets are global side-files, so they work **across colonies and save games**. Because policies
are colony-specific objects (not defs), a preset stores each policy's **content** and, on apply,
**matches a policy by name** in the destination colony — assigning the existing one, or recreating
it with the saved content if it doesn't exist (one shared policy per name, never duplicated).

### Free Will / Free-ish Will (optional)
If installed, the **work-priorities axis is automatically skipped** for colonists Free Will manages
(it continuously rewrites priorities, so a default would just be overwritten). Schedule and
policies — which Free Will doesn't touch — still apply. Toggleable in settings.

### Combat Extended (compatible)
Pawn defaults only set vanilla schedule / work / policy / player-settings state. They **never**
touch CE's inventory, loadout, or ammo "loading" system, and never re-patch CE-owned methods.

## Policy defaults across saves

Apparel/outfit, drug, food and reading **policies** are stored inside each colony save, so they
normally do not follow you to a new game. The **Policies & bills** settings tab adds:

- **Save this colony's policies** — captures all four policy databases (each policy's name +
  portable content, and which one is the colony default) into a global side-file.
- **Apply to this colony** — recreates the saved policies by name in the current colony (matching an
  existing same-name policy as-is, never overwriting its content) and restores the saved default.
- An optional **"apply once per new game"** toggle (off by default) that applies the saved set the
  first time you play each new game.
- A **per-kind colony-default picker**: for each kind, a radio list of the current colony's policies
  marks one as the colony default — the policy brand-new colonists inherit. This calls the vanilla
  `OutfitDatabase.SetDefault` / `DrugPolicyDatabase.SetDefault` / `FoodRestrictionDatabase.SetDefault`
  / `ReadingPolicyDatabase.SetDefault` (the chosen policy moves to index 0).
- A **Create / edit…** button per kind that opens the vanilla policy-management dialog, so creating
  and editing the policies themselves uses RimWorld's own (DLC/mod-accurate) editor.

## More than 15 bills per workbench

RimWorld caps each workbench at 15 bills. The **Policies & bills** tab can **raise that cap** on its
own (a self-contained Harmony transpiler on `BillStack.DoListing` + `ITab_Bills.FillTab`) to a value
you choose (default **50**, range 15–250). If **No Max Bills** / **No Max Bills Redux** is installed
the built-in raiser stays **off** (no double-patch) and defers to that mod. A single
`EffectiveBillCap()` drives both the visible UI cap and the number of bills restored from a saved
default, so what you saved is what you get back. A workbench runs *all* of its bills each tick, so a
very high cap can affect performance.

## Building defaults

Build a new kitchen, stonecutter, smithy, machining table, etc. and it is automatically populated
with the bills you saved as *the default for that type of workbench*. Storage buildings (shelves
and the like) can have their item filter + priority saved and applied the same way.

Select a building — controls appear in two places (both toggleable in settings):

- A **"Defaults" button** in the building's **Bills tab** (for bill givers).
- A **"Defaults" command button** (gizmo) on the selected building.

Both open a menu to **save** the current setup as a named preset, **apply** a preset (bills are
replaced), or **manage** presets. Freshly-built workbenches get their default bills automatically;
storage settings are *not* auto-applied on build (that would overwrite a relocated building) —
use **Apply**, or a blueprint.

### Project RimFactory Revived (optional)
- **S.A.L. assemblers / programmable assemblers / miners** get default bills like vanilla benches.
- **Storage machines** (cold storage / DSU / shelves, pullers, smart hoppers) get storage defaults.
- **Conveyor splitters** get their per-direction routing + filters saved/applied.

### Blueprints Forked (optional)
- A **blueprint remembers each captured building's bills and storage settings** and recreates them
  when built — **even across different save games**. Works with Project RimFactory machines too.

## Where defaults are stored

- **Pawn presets**: `…\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\PawnDefaults\Pawns.xml`
- **Default policies (cross-save)**: `…\RimWorld by Ludeon Studios\PawnDefaults\PolicyDefaults.xml`
- **Per-building-type defaults**: per-`defName` XML files in `…\RimWorld by Ludeon Studios\WorkbenchBillDefaults\`
- **Blueprint-embedded config**: inside the blueprint itself (colony save + Blueprints Forked XML).

Both default stores are shared across all colonies and persist across save/quit. The pawn feature
records *which* colonists it has already defaulted inside the colony save (a small auto-component),
so each pawn is configured exactly once. Nothing else is written into your save by these features.

## Portability notes

- Building presets drop colony-specific bill settings (a *specific worker* restriction; a *take to
  a specific stockpile* target is reduced to *best stockpile*). The *Slaves/Mechs/Non-mechs only*
  category is portable and kept. Recipes locked behind research/DLC are skipped at apply time.
- Pawn presets store schedule/work as portable defs/ints and policies as portable content
  (filters / drug entries by def), matched or recreated by name in the target colony.

**Known limitation (buildings):** a Project RimFactory **conveyor splitter's** routing is captured
in absolute directions; re-check output directions if you rotate/flip a blueprint containing one.

## Performance

Deliberately minimal. Building: the build-time hook bails instantly for anything that can't hold a
config; defaults are read once per type and cached. Pawn: defaults apply once per colonist (guarded
by a persisted marker); a throttled component scan (every 250 ticks) only re-checks free colonists
to catch late-initialising pawns and children reaching working age.

## Requirements

- RimWorld 1.6
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)

Load **after** Harmony and (if used) Better Workbench Management, Project RimFactory, Blueprints
Forked, Free Will / Free-ish Will, and Combat Extended.

## Building from source

The project builds with the .NET SDK via the SDK-style `Source/BetterWorkbenchDefaults.csproj`:

```
dotnet build -c Release Source/BetterWorkbenchDefaults.csproj
```

It globs `Source/*.cs` and references the installed RimWorld + Harmony assemblies, writing
`Assemblies/BetterWorkbenchDefaults.dll`. RimWorld must be closed to overwrite the assembly. The
legacy `Source/build.ps1` (Windows-only Roslyn invocation) is kept for reference but is no longer
used.
