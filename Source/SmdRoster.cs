using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The known-colonist roster of the running game (M1): the pawn default is applied exactly once per colonist. The data lives in
    /// <see cref="SmdGameData"/> (saved inside JB Framework's game component); this object is the view the apply code works with.
    /// <see cref="Current"/> is null while no game runs. The game hooks (new game, load, the throttled retry scan) are called from
    /// <see cref="WBD_Mod"/>'s OnGameStarted / OnGameLoaded / OnTick, so no component class of this mod exists in a running game.
    /// </summary>
    public sealed class SmdRoster
    {
        static readonly SmdRoster instance = new SmdRoster();
        SmdRoster() { }

        /// <summary>The roster of the running game, or null (main menu).</summary>
        public static SmdRoster Current => Verse.Current.Game != null ? instance : null;

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
        static void TakeBaseline(bool newGame)
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

        /// <summary>A save was loaded (old saves: their components were read by now).</summary>
        internal static void GameLoaded()
        {
            SmdGameData.MoveLegacyPending();
            TakeBaseline(newGame: false);
        }

        /// <summary>A new game started: the map and the starting colonists exist, the first tick has not run.</summary>
        internal static void GameStarted()
        {
            // Starting colonists were spawned during map generation, before this call, and were processed as new
            // by the spawn hook (and are known since). Everything from here on is judged by the roster.
            TakeBaseline(newGame: true);
            if (SmdGameData.startCard == SmdGameData.StartNone)
                SmdGameData.startCard = SmdGameData.StartNewColony; // the welcome card offers profiles once
            if (!SmdGameData.colonyDefaultsApplied)
            {
                SmdGameData.colonyDefaultsApplied = true;
                ColonyDefaults.ApplyAtNewGame(instance);
            }
        }

        /// <summary>Every 250 ticks: retry the colonist default for pawns flagged new (never for pre-existing colonists).</summary>
        internal static void RetryScan()
        {
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
    }

    /// <summary>
    /// Per-map data of Set my Defaults (inside JB Framework's map component, <c>KitRegistry.MapData</c>): the configs queued by a
    /// blueprint placement (<see cref="SmdPending"/>). A removed map takes its data along.
    /// </summary>
    public static class SmdMapData
    {
        static readonly ConditionalWeakTable<Map, List<PendingEntry>> pending = new ConditionalWeakTable<Map, List<PendingEntry>>();

        /// <summary>The map's pending list (created on first use).</summary>
        public static List<PendingEntry> For(Map map) => pending.GetValue(map, _ => new List<PendingEntry>());

        /// <summary>The map's pending list, or null when it has none (no allocation).</summary>
        public static List<PendingEntry> Peek(Map map) => map != null && pending.TryGetValue(map, out var l) ? l : null;

        /// <summary>Count over every live map (diagnosis, tests).</summary>
        public static int CountAll()
        {
            int n = 0;
            if (Find.Maps != null) foreach (var m in Find.Maps) n += Peek(m)?.Count ?? 0;
            return n;
        }

        /// <summary>KitRegistry.MapData: one map's node.</summary>
        public static void Expose(Map map)
        {
            if (map == null) return;
            List<PendingEntry> list = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                list = Peek(map);
                if (list != null) list.RemoveAll(e => e == null || e.def == null || e.snapshot == null || e.snapshot.IsEmpty);
                if (list == null || list.Count == 0) return;   // nothing queued: no node at all
            }
            Scribe_Collections.Look(ref list, "pending", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && list != null)
            {
                list.RemoveAll(e => e == null);
                For(map).AddRange(list);
            }
        }

        /// <summary>KitRegistry.MapData reset: a new or loading map, or a removed one.</summary>
        public static void Reset(Map map)
        {
            if (map != null) pending.Remove(map);
        }

        /// <summary>A new Game object: nothing of the previous game's maps is kept.</summary>
        internal static void Clear() => pending.Clear();
    }
}
