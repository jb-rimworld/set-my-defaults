using System;
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
        /// <summary>
        /// Entry point. <paramref name="fromHook"/> is true when called from a spawn/faction hook
        /// (a genuine "new pawn" signal) and false from the retry scan. The scan only re-processes
        /// pawns a hook already flagged as new, so pre-existing colonists are never auto-defaulted.
        /// </summary>
        public static void TryApply(Pawn p, bool fromHook)
        {
            // Never touch trackers or read/write side-files while a game is loading/saving.
            if (Scribe.mode != LoadSaveMode.Inactive)
                return;

            var settings = WBD_Mod.Settings;
            if (settings == null || !settings.autoApplyToNewPawns)
                return;
            if (!IsEligible(p))
                return;

            var comp = Current.Game?.GetComponent<PawnDefaultsGameComponent>();
            if (comp == null)
                return;

            int id = p.thingIDNumber;

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

        /// <summary>Age gate for the "exclude children" option (true once the pawn is an adult).</summary>
        private static bool IsAdult(Pawn p)
        {
            return p.DevelopmentalStage.Adult();
        }
    }
}
