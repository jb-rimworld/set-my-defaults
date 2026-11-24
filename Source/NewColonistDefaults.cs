using System;
using System.Linq;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The single, idempotent entry point that applies the default pawn preset to a genuinely-new
    /// player colonist exactly once. Called from the spawn hook, the faction-change hook, and the
    /// GameComponent scan; the per-save marker sets guarantee once-only semantics across all of them.
    /// </summary>
    public static class NewColonistDefaults
    {
        /// <summary>Where a call to <see cref="TryApply(Pawn, JoinSource)"/> comes from.</summary>
        public enum JoinSource
        {
            /// <summary>Pawn.SpawnSetup without respawningAfterLoad (map gen, birth, arrival, but ALSO caravans coming home,
            /// pods, caskets, gravship landings: only an unknown pawn counts as new here).</summary>
            Spawn,
            /// <summary>Pawn.SetFaction to the player (recruit, join, enslave): new unless the save already knew the pawn.</summary>
            Faction,
            /// <summary>The 250-tick retry scan: only pawns a hook already flagged as new.</summary>
            Scan
        }

        /// <summary>Back-compat overload (1.x callers): hook = spawn, otherwise scan.</summary>
        public static void TryApply(Pawn p, bool fromHook) => TryApply(p, fromHook ? JoinSource.Spawn : JoinSource.Scan);

        /// <summary>
        /// Entry point. A pawn is "new" exactly once per save: the first time a hook sees it as a player pawn that the
        /// known roster (<see cref="PawnDefaultsGameComponent"/>) does not contain. Pre-existing colonists are known
        /// from the roster baseline, so they are never auto-defaulted, however often they leave and re-enter a map.
        /// </summary>
        public static void TryApply(Pawn p, JoinSource source)
        {
            // Never touch trackers or read/write side-files while a game is loading/saving.
            if (Scribe.mode != LoadSaveMode.Inactive)
                return;
            if (p == null || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike)
                return;
            if (p.Faction != Faction.OfPlayerSilentFail)
                return;

            var comp = Current.Game?.GetComponent<PawnDefaultsGameComponent>();
            if (comp == null)
                return;

            int id = p.thingIDNumber;
            bool fromHook = source != JoinSource.Scan;

            if (fromHook)
            {
                // Roster first, BEFORE any setting check: a pawn must become known even while auto-apply is off,
                // otherwise switching it on later would treat every returning colonist as new (B1).
                if (comp.IsKnown(id))
                {
                    if (!comp.IsSeen(id))
                        return; // an existing colonist re-entering a map: never touched
                }
                else
                {
                    comp.MarkKnown(id);
                    // Safety net for pawns the baseline scan could not see: joined the colony before the baseline.
                    if (source == JoinSource.Spawn && comp.BaselineTaken && p.playerSettings != null
                        && p.playerSettings.joinTick < comp.BaselineTick)
                        return;
                }
            }

            var settings = WBD_Mod.Settings;
            if (settings == null || !settings.autoApplyToNewPawns || !SmdSections.On(SmdSections.Pawns))
                return;
            if (!IsEligible(p))
                return;

            // The scan is retry-only: never let it discover a pawn no hook has flagged as new.
            if (!fromHook && !comp.IsSeen(id))
                return;

            var config = PawnDefaultsStore.GetDefaultConfig();
            if (config == null || config.IsEmpty)
                return;

            // Flag genuinely-new pawns only once a default actually exists, so creating the first
            // preset later does NOT retroactively apply it to colonists who joined beforehand.
            if (fromHook)
                comp.MarkSeen(id);

            try
            {
                Apply(p, comp, config, settings);
            }
            catch (Exception e)
            {
                WBD_Log.Error("Error applying pawn defaults to " + (p?.LabelShortCap.ToString() ?? "?") + ": " + e);
            }
        }

        private static bool IsEligible(Pawn p)
        {
            if (p == null || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike)
                return false;
            if (p.Faction != Faction.OfPlayerSilentFail)
                return false;
            if (p.IsColonyMech)
                return false;
            // IsColonist is true only for free player humanlikes (excludes slaves, prisoners, guests).
            if (!p.IsColonist)
                return false;
            return true;
        }

        private static void Apply(Pawn p, PawnDefaultsGameComponent comp, PawnConfigSnapshot config, WBD_Settings settings)
        {
            int id = p.thingIDNumber;

            var enabled = settings.EnabledPawnAxes();
            var coreAxes = enabled & ~PawnAxis.Work;
            bool workEnabled = (enabled & PawnAxis.Work) != 0;

            // Fast skip once a pawn has everything it will get.
            bool coreDone = coreAxes == PawnAxis.None || comp.CoreApplied(id);
            bool workDone = !workEnabled || comp.WorkApplied(id);
            if (coreDone && workDone)
                return;

            bool isAdult = IsAdult(p);

            // When children are excluded, defer EVERYTHING until the pawn is an adult, then apply
            // in one go. (This is an age gate — NOT "can work", so incapable adults are not excluded.)
            if (!settings.pawnIncludeChildren && !isAdult)
                return;

            // --- Core axes (schedule / policies / hostility / medical) ---
            // Needs the relevant trackers (timetable/outfits/...), NOT workSettings. The timetable
            // tracker is created with the others; if it is not ready yet (very early spawn / newborn)
            // bail without marking so the scan retries when the pawn is fully set up.
            if (coreAxes != PawnAxis.None && !comp.CoreApplied(id) && p.timetable != null)
            {
                config.ApplyTo(p, coreAxes);
                comp.MarkCore(id);
            }

            // --- Work priorities ---
            if (workEnabled && !comp.WorkApplied(id))
            {
                if (p.workSettings != null && WorkPrioritiesTemplate.CanWork(p))
                {
                    bool yieldToFreeWill = settings.pawnSkipWorkUnderFreeWill && FreeWillCompat.ManagesWork(p);
                    if (!yieldToFreeWill)
                        config.ApplyTo(p, PawnAxis.Work);
                    // Mark either way: if Free Will owns this pawn, applying would just be reverted,
                    // so we yield once and stop re-checking (the player can still apply via gizmo).
                    comp.MarkWork(id);
                }
                else if (isAdult)
                {
                    // An adult who can do no work types will never get work priorities; mark it done
                    // so the scan stops re-checking. (A child is left unmarked so it gets work later.)
                    comp.MarkWork(id);
                }
            }
        }

        /// <summary>
        /// First-start card: after a profile changed the default colonist preset, the starting colonists get the new default once
        /// (they were defaulted with the old one seconds before, while the map was generated). Same rules as for a new colonist
        /// (axes, children, Free Will). Returns how many colonists were changed.
        /// </summary>
        public static int ApplyDefaultToStartingColonists() => ApplyDefaultToStartingColonists(null);

        /// <summary>The same with a given colonist preset (a profile's default, read straight from the profile); null = the library's.</summary>
        public static int ApplyDefaultToStartingColonists(PawnConfigSnapshot preset)
        {
            var comp = Current.Game?.GetComponent<PawnDefaultsGameComponent>();
            var settings = WBD_Mod.Settings;
            if (comp == null || settings == null || !settings.autoApplyToNewPawns || !SmdSections.On(SmdSections.Pawns))
                return 0;
            var config = preset ?? PawnDefaultsStore.GetDefaultConfig();
            if (config == null || config.IsEmpty)
                return 0;
            int n = 0;
            foreach (var p in PawnsFinder.AllMaps_FreeColonists.ToList())
            {
                if (!IsEligible(p) || (!settings.pawnIncludeChildren && !IsAdult(p)))
                    continue;
                try
                {
                    var axes = settings.EnabledPawnAxes();
                    bool work = (axes & PawnAxis.Work) != 0 && p.workSettings != null && WorkPrioritiesTemplate.CanWork(p)
                        && !(settings.pawnSkipWorkUnderFreeWill && FreeWillCompat.ManagesWork(p));
                    if (!work)
                        axes &= ~PawnAxis.Work;
                    if (axes == PawnAxis.None || p.timetable == null)
                        continue;
                    config.ApplyTo(p, axes);
                    comp.MarkKnown(p.thingIDNumber); comp.MarkSeen(p.thingIDNumber); comp.MarkCore(p.thingIDNumber);
                    if (work) comp.MarkWork(p.thingIDNumber);
                    n++;
                }
                catch (Exception e)
                {
                    WBD_Log.Warn("Could not apply the colonist preset to " + (p?.LabelShortCap.ToString() ?? "?") + ": " + e.Message);
                }
            }
            return n;
        }

        /// <summary>Age gate for the "exclude children" option (true once the pawn is an adult).</summary>
        private static bool IsAdult(Pawn p)
        {
            return p.DevelopmentalStage.Adult();
        }
    }
}
