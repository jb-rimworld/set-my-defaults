using System.Collections.Generic;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Per-save bookkeeping so the pawn default is applied exactly once per colonist, surviving
    /// save/load. Auto-instantiated by RimWorld (no def needed). Two marker sets keyed by
    /// <c>pawn.thingIDNumber</c>:
    ///  - <see cref="appliedCore"/>: schedule/policies/hostility/medical were applied;
    ///  - <see cref="appliedWork"/>: work priorities were applied (tracked separately so a child
    ///    born into the colony still gets work priorities later, once it can work).
    /// A throttled scan re-checks free colonists to catch pawns whose trackers were not ready at the
    /// spawn/recruit hook and children who have since reached working age.
    /// </summary>
    public class PawnDefaultsGameComponent : GameComponent
    {
        // Pawns a join/spawn hook flagged as genuinely-new while a default existed. The scan only
        // RETRIES these — it must never DISCOVER pawns itself, or enabling the mod (or saving the
        // first preset) in an existing colony would stomp every current colonist's settings.
        private List<int> seenList = new List<int>();
        private HashSet<int> seen = new HashSet<int>();

        private List<int> appliedCoreList = new List<int>();
        private List<int> appliedWorkList = new List<int>();
        private HashSet<int> appliedCore = new HashSet<int>();
        private HashSet<int> appliedWork = new HashSet<int>();

        // True once the saved cross-save default policies have been auto-applied to THIS game (so the
        // optional "apply once per new game" toggle never re-applies on every reload of the same colony).
        private bool policyDefaultsApplied;

        public PawnDefaultsGameComponent(Game game) { }

        public bool IsSeen(int id) => seen.Contains(id);
        public bool CoreApplied(int id) => appliedCore.Contains(id);
        public bool WorkApplied(int id) => appliedWork.Contains(id);

        /// <summary>Flag a genuinely-new pawn so the retry scan is allowed to (re)process it.</summary>
        public void MarkSeen(int id)
        {
            if (seen.Add(id))
                seenList.Add(id);
        }

        /// <summary>Mark core-applied; returns true the first time only.</summary>
        public bool MarkCore(int id)
        {
            if (!appliedCore.Add(id))
                return false;
            appliedCoreList.Add(id);
            return true;
        }

        /// <summary>Mark work-applied; returns true the first time only.</summary>
        public bool MarkWork(int id)
        {
            if (!appliedWork.Add(id))
                return false;
            appliedWorkList.Add(id);
            return true;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null || Find.TickManager.TicksGame % 250 != 0)
                return;
            var settings = WBD_Mod.Settings;
            if (settings == null)
                return;

            // Optional one-shot: apply the saved cross-save default policies to this colony exactly once.
            if (!policyDefaultsApplied && settings.policyAutoApplyOnNewGame
                && Scribe.mode == LoadSaveMode.Inactive
                && PolicyDefaultsStore.HasSet())
            {
                if (PolicyDefaultsStore.ApplyToCurrentColony())
                    policyDefaultsApplied = true;
            }

            if (!settings.autoApplyToNewPawns)
                return;
            if (!PawnDefaultsStore.HasPresets())
                return;

            var maps = Find.Maps;
            if (maps == null)
                return;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m]?.mapPawns?.FreeColonists;
                if (pawns == null)
                    continue;
                // Retry only: TryApply(fromHook:false) skips any pawn not already flagged seen,
                // so the scan never applies defaults to pre-existing colonists.
                for (int i = 0; i < pawns.Count; i++)
                    NewColonistDefaults.TryApply(pawns[i], fromHook: false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref seenList, "seen", LookMode.Value);
            Scribe_Collections.Look(ref appliedCoreList, "appliedCore", LookMode.Value);
            Scribe_Collections.Look(ref appliedWorkList, "appliedWork", LookMode.Value);
            Scribe_Values.Look(ref policyDefaultsApplied, "policyDefaultsApplied", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Rebuild();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Rebuild();
        }

        private void Rebuild()
        {
            seenList = seenList ?? new List<int>();
            appliedCoreList = appliedCoreList ?? new List<int>();
            appliedWorkList = appliedWorkList ?? new List<int>();
            seen = new HashSet<int>(seenList);
            appliedCore = new HashSet<int>(appliedCoreList);
            appliedWork = new HashSet<int>(appliedWorkList);
        }
    }
}
