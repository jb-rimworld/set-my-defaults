using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Per-save bookkeeping so the pawn default is applied exactly once per colonist. Since 2.0 the data itself lives in
    /// <see cref="SmdGameData"/> (saved inside JB Framework's component, removal-safe). This component keeps its old class name so
    /// saves from before 2.0 load: its old node is read once and merged; it is never written again (Patch_Game_ExposeSmallComponents).
    /// It still provides the game hooks (new game, load, the throttled retry scan).
    /// </summary>
    public class PawnDefaultsGameComponent : GameComponent
    {
        public PawnDefaultsGameComponent(Game game) { }

        public bool IsSeen(int id) => SmdGameData.seen.Contains(id);
        public bool IsKnown(int id) => SmdGameData.known.Contains(id);
        public bool BaselineTaken => SmdGameData.rosterVersion >= 1;
        public int BaselineTick => SmdGameData.baselineTick;
        public bool ColonyDefaultsApplied => SmdGameData.colonyDefaultsApplied;
        public bool CoreApplied(int id) => SmdGameData.appliedCore.Contains(id);
        public bool WorkApplied(int id) => SmdGameData.appliedWork.Contains(id);

        public void MarkKnown(int id) => SmdGameData.known.Add(id);
        /// <summary>Flag a genuinely-new pawn so the retry scan is allowed to (re)process it.</summary>
        public void MarkSeen(int id) => SmdGameData.seen.Add(id);
        /// <summary>Mark core-applied; returns true the first time only.</summary>
        public bool MarkCore(int id) => SmdGameData.appliedCore.Add(id);
        /// <summary>Mark work-applied; returns true the first time only.</summary>
        public bool MarkWork(int id) => SmdGameData.appliedWork.Add(id);

        /// <summary>
        /// Take the roster baseline: every player-faction humanlike that exists right now (maps, held in caskets/containers,
        /// caravans and other world pawns, transporters, the travelling gravship) is known. Once per save.
        /// </summary>
        private static void TakeBaseline(bool newGame)
        {
            if (SmdGameData.rosterVersion >= 1)
                return;
            int added = 0;
            if (!newGame)
            {
                try
                {
                    foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                    {
                        if (p != null && p.RaceProps != null && p.RaceProps.Humanlike && p.Faction == Faction.OfPlayerSilentFail
                            && SmdGameData.known.Add(p.thingIDNumber))
                            added++;
                    }
                }
                catch (System.Exception e)
                {
                    WBD_Log.Warn("Could not scan existing colonists for the known roster: " + e.Message);
                }
            }
            SmdGameData.baselineTick = newGame ? 0 : (Find.TickManager?.TicksGame ?? 0);
            SmdGameData.rosterVersion = 1;
            if (added > 0)
                WBD_Log.Msg("Known roster: " + added + " existing colonists recorded; they will never be auto-defaulted.");
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            SmdGameData.MergePendingLegacy(Current.Game);
            TakeBaseline(newGame: false);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            // Starting colonists were spawned during map generation, before this call, and were processed as new
            // by the spawn hook (and are known since). Everything from here on is judged by the roster.
            TakeBaseline(newGame: true);
            if (SmdGameData.startCard == SmdGameData.StartNone)
                SmdGameData.startCard = SmdGameData.StartNewColony; // the first-start card offers profiles once
            if (!SmdGameData.colonyDefaultsApplied)
            {
                SmdGameData.colonyDefaultsApplied = true;
                ColonyDefaults.ApplyAtNewGame(this);
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null || Find.TickManager.TicksGame % 250 != 0)
                return;
            var settings = WBD_Mod.Settings;
            if (settings == null)
                return;
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
                // Retry only: TryApply skips any pawn not already flagged seen, so the scan never applies defaults to
                // pre-existing colonists.
                for (int i = 0; i < pawns.Count; i++)
                    NewColonistDefaults.TryApply(pawns[i], NewColonistDefaults.JoinSource.Scan);
            }
        }

        /// <summary>Reads the node of a save made before 2.0 (or by the 2.0 test builds) and merges it. Never writes.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;
            List<int> seenL = null, coreL = null, workL = null, knownL = null;
            int version = 0, baseline = 0;
            bool colony = false, policy = false;
            Scribe_Collections.Look(ref seenL, "seen", LookMode.Value);
            Scribe_Collections.Look(ref coreL, "appliedCore", LookMode.Value);
            Scribe_Collections.Look(ref workL, "appliedWork", LookMode.Value);
            Scribe_Values.Look(ref policy, "policyDefaultsApplied", false);
            Scribe_Collections.Look(ref knownL, "known", LookMode.Value);
            Scribe_Values.Look(ref version, "rosterVersion", 0);
            Scribe_Values.Look(ref baseline, "baselineTick", 0);
            Scribe_Values.Look(ref colony, "colonyDefaultsApplied", false);
            // FW-REQUEST (11): JB Framework's game component does not exist in a save from before 2.0; vanilla creates it right AFTER
            // the saved components were read (Game.ExposeSmallComponents → FillComponents) and its constructor resets every mod's
            // SaveData. Merging here would be wiped, so the old roster is parked and merged in LoadedGame (after that reset).
            SmdGameData.ParkLegacyRoster(Current.Game, seenL, coreL, workL, knownL, version, baseline, colony, policy);
        }
    }
}
