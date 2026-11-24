using System.Collections.Generic;
using RimWorld;
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

        // Legacy (1.x) flag of the tick-250 "apply policies once" path. 2.0 applies colony defaults only in
        // StartedNewGame, so this is just carried along (written true) and never read for decisions.
        private bool policyDefaultsApplied;

        // ---- Known roster (2.0, M1) ----
        // Every player-faction humanlike this save has already met. A pawn in here is never treated as "new"
        // again, so a caravan coming home, a transport pod landing, a cryptosleep casket opening or a gravship
        // landing (all of which run SpawnSetup with respawningAfterLoad=false) never re-applies defaults.
        private List<int> knownList = new List<int>();
        private HashSet<int> known = new HashSet<int>();
        // 0 = save from before 2.0 (or a new game still generating its map); 1 = roster baseline taken.
        private int rosterVersion;
        // Tick at which the baseline was taken. A pawn that joined the player faction before it (joinTick < this)
        // is old, even if the baseline scan missed it.
        private int baselineTick;
        // True once the colony settings / policy set were applied to this game at its start (M8).
        private bool colonyDefaultsApplied;

        public PawnDefaultsGameComponent(Game game) { }

        public bool IsSeen(int id) => seen.Contains(id);
        public bool IsKnown(int id) => known.Contains(id);
        public bool BaselineTaken => rosterVersion >= 1;
        public int BaselineTick => baselineTick;
        public bool ColonyDefaultsApplied => colonyDefaultsApplied;

        public void MarkKnown(int id)
        {
            if (known.Add(id))
                knownList.Add(id);
        }

        /// <summary>
        /// Take the roster baseline: every player-faction humanlike that exists right now (maps, held in
        /// caskets/containers, caravans and other world pawns, transporters, the travelling gravship) is known.
        /// Runs once per save: on the first load with 2.0, or at the start of a new game.
        /// </summary>
        private void TakeBaseline(bool newGame)
        {
            if (rosterVersion >= 1)
                return;
            int added = 0;
            if (!newGame)
            {
                try
                {
                    foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                    {
                        if (p != null && p.RaceProps != null && p.RaceProps.Humanlike && p.Faction == Faction.OfPlayerSilentFail
                            && known.Add(p.thingIDNumber))
                        {
                            knownList.Add(p.thingIDNumber);
                            added++;
                        }
                    }
                }
                catch (System.Exception e)
                {
                    WBD_Log.Warn("Could not scan existing colonists for the known roster: " + e.Message);
                }
            }
            baselineTick = newGame ? 0 : (Find.TickManager?.TicksGame ?? 0);
            rosterVersion = 1;
            if (added > 0)
                WBD_Log.Msg("Known roster: " + added + " existing colonists recorded; they will never be auto-defaulted.");
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            TakeBaseline(newGame: false);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            // Starting colonists were spawned during map generation, before this call, and were processed as new
            // by the spawn hook (and are known since). Everything from here on is judged by the roster.
            TakeBaseline(newGame: true);
            if (!colonyDefaultsApplied)
            {
                colonyDefaultsApplied = true;
                ColonyDefaults.ApplyAtNewGame(this);
            }
        }
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

            // (2.0: the old "apply saved policies once" path that ran here on EVERY save is gone. Colony defaults
            // are applied only in StartedNewGame, before the starting colonists settle. Fixes B6.)

            if (!settings.autoApplyToNewPawns || !SmdSections.On(SmdSections.Pawns))
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
                    NewColonistDefaults.TryApply(pawns[i], NewColonistDefaults.JoinSource.Scan);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref seenList, "seen", LookMode.Value);
            Scribe_Collections.Look(ref appliedCoreList, "appliedCore", LookMode.Value);
            Scribe_Collections.Look(ref appliedWorkList, "appliedWork", LookMode.Value);
            Scribe_Values.Look(ref policyDefaultsApplied, "policyDefaultsApplied", false);
            Scribe_Collections.Look(ref knownList, "known", LookMode.Value);
            Scribe_Values.Look(ref rosterVersion, "rosterVersion", 0);
            Scribe_Values.Look(ref baselineTick, "baselineTick", 0);
            Scribe_Values.Look(ref colonyDefaultsApplied, "colonyDefaultsApplied", false);
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
            knownList = knownList ?? new List<int>();
            known = new HashSet<int>(knownList);
            seen = new HashSet<int>(seenList);
            appliedCore = new HashSet<int>(appliedCoreList);
            appliedWork = new HashSet<int>(appliedWorkList);
        }
    }
}
