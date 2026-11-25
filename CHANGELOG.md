# Changelog

## 2.0.0 (2026-11-25)
- The Defaults button opens your library in game: every saved preset with preview, rename, delete and the default marked, plus a note when a preset needs a mod you are not running.
- Profiles: save your whole setup under a name ("Tribal start") and apply it later; applying one asks first when it would replace presets of the same name, and your previous versions are kept as a profile "Before …".
- Share strings: copy a profile or the whole library as one text string and paste a friend's; you see what it holds and what it needs before anything is stored.
- A new colony opens a welcome card: start with your defaults or one of your profiles, and choose what happens by itself. "Also bring my saved policies" starts from your setting for new games. A profile that would replace presets of the same name asks first; "Use my defaults instead" leaves your library alone.
- Your presets are safe: two games open at the same time no longer overwrite each other's new presets, a library from a newer version of the mod keeps everything this version does not know, and a damaged library file is never overwritten (the game uses the backup meanwhile, and "Restore the backup" puts it back).
- New settings window (JB Framework): a sidebar with one page per area, search, presets and a Compatibility page. Every switch has a short explanation, in English and German. Each integration (Nice Bill Tab, Project RimFactory, Blueprints Forked) is on while that mod is present and can be switched off on the Compatibility page.
- Each area (workbenches and shelves, zones, colonists, policies, colony settings, library) can be switched off as a whole. Saved presets are kept.
- Settings are kept per colony; new colonies start from your defaults. Your old settings are carried over.
- All presets now live in one library outside your saves. Presets from older versions are imported once; the old files are not changed.
- Presets that need a mod you removed are kept, say which mod they need, and the rest of the preset still applies.
- Colonists you already have are never re-defaulted, also after a caravan, transport pods or a cryptosleep casket. Saves from the previous version keep their record of who was defaulted, so a child still gets its work priorities once it is old enough.
- Applying a preset works on every selected building or zone of the same kind at once.
- Raising the bill limit takes effect immediately, no restart. With No Max Bills Redux installed but not working (its 1.6 version on RimWorld 1.7), Set my Defaults lifts the limit itself instead of leaving you at 15.
- Bills keep their custom name, style and "drop in / count in" stockpile.
- Zones have their own Defaults button switch.
- New shelves get their default filter, new hydroponics and growing zones their default plant, new stockpiles their default filter.
- Filters remember whole categories, so items a mod adds later are included.
- Related buildings share defaults (the fueled stove uses the electric stove's bills when it has none of its own).
- Colony settings and the policy set can be saved and applied to new games.
- Saves no longer contain a class of this mod: removing it later leaves no errors, and there is nothing to prepare. Saves of the previous version load with everything intact.
- When the library cannot be written, the reason is shown in your language.
- Needs JB Framework. When JB Framework is missing, the game offers to install it. Built and tested on RimWorld 1.7. The 1.6 build is best effort and untested: 1.7 is the focus; RimWorld 1.6 gets the same 2.0 version, built for 1.6 but never run there.
