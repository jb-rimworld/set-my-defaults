using System.Collections.Generic;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Everything Set my Defaults keeps per save (2.0): the known colonist roster (M1), the "colony defaults applied" flag (M8) and
    /// the welcome-card state. Saved through JB Framework's game component (<c>KitRegistry.SaveData</c>); the pending blueprint configs
    /// live per map in <see cref="SmdMapData"/> (<c>KitRegistry.MapData</c>). A save made with 2.0 holds no class of this mod and loads
    /// cleanly after the mod is removed.
    ///
    /// Saves made before 2.0 hold the old <see cref="PawnDefaultsGameComponent"/> and <see cref="WBD_PendingConfigs"/> nodes; those
    /// classes still exist (registered with <c>KitRegistry.LegacyComponent</c>), read their old nodes once and merge them in here
    /// (union); the framework never writes them again and drops them after the load.
    /// </summary>
    public static class SmdGameData
    {
        // ---- roster (M1) ----
        internal static HashSet<int> seen = new HashSet<int>();
        internal static HashSet<int> appliedCore = new HashSet<int>();
        internal static HashSet<int> appliedWork = new HashSet<int>();
        internal static HashSet<int> known = new HashSet<int>();
        /// <summary>0 = no baseline yet (save from before 2.0, or a new game still generating its map); 1 = baseline taken.</summary>
        internal static int rosterVersion;
        /// <summary>Tick of the baseline. A pawn that joined the player faction before it is old.</summary>
        internal static int baselineTick;
        /// <summary>True once the colony settings / policy set were applied to this game at its start (M8).</summary>
        internal static bool colonyDefaultsApplied;
        /// <summary>Carried along from 1.x (the old tick-250 policy path); never read for decisions.</summary>
        internal static bool policyDefaultsApplied;
        /// <summary>The policy set was applied at this game's start ("apply the saved policies to new games").</summary>
        internal static bool policiesAppliedAtStart;

        // ---- first-start card (M7) ----
        /// <summary>0 = no card for this save (old save, or the card belongs to another state); 1 = new colony, card due;
        /// 2 = the mod joined this existing save, card due; 3 = done (choice applied once).</summary>
        internal static int startCard;
        /// <summary>What the card applied: "" = my defaults, else the profile name; null before the card was done.</summary>
        internal static string startChoice;
        public const int StartNone = 0, StartNewColony = 1, StartJoined = 2, StartDone = 3;

        // ---- pending blueprint configs of a save written by 2.0 before they moved to the per-map data (map uniqueID) ----
        /// <summary>Read from the game node of such a save, moved to <see cref="SmdMapData"/> in <see cref="MoveLegacyPending"/>; never written.</summary>
        internal static Dictionary<int, List<PendingEntry>> legacyPending = new Dictionary<int, List<PendingEntry>>();

        public static int KnownCount => known.Count;

        /// <summary>A new Game object (new game, or before a load): forget the previous one.</summary>
        public static void Reset()
        {
            seen = new HashSet<int>();
            appliedCore = new HashSet<int>();
            appliedWork = new HashSet<int>();
            known = new HashSet<int>();
            rosterVersion = 0;
            baselineTick = 0;
            colonyDefaultsApplied = false;
            policyDefaultsApplied = false;
            policiesAppliedAtStart = false;
            startCard = StartNone;
            startChoice = null;
            legacyPending = new Dictionary<int, List<PendingEntry>>();
            SmdMapData.Clear();
        }

        /// <summary>Saved inside the framework's component under this mod's packageId.</summary>
        public static void Expose()
        {
            var seenL = new List<int>(seen);
            var coreL = new List<int>(appliedCore);
            var workL = new List<int>(appliedWork);
            var knownL = new List<int>(known);
            Scribe_Collections.Look(ref seenL, "seen", LookMode.Value);
            Scribe_Collections.Look(ref coreL, "appliedCore", LookMode.Value);
            Scribe_Collections.Look(ref workL, "appliedWork", LookMode.Value);
            Scribe_Collections.Look(ref knownL, "known", LookMode.Value);
            Scribe_Values.Look(ref rosterVersion, "rosterVersion", 0);
            Scribe_Values.Look(ref baselineTick, "baselineTick", 0);
            Scribe_Values.Look(ref colonyDefaultsApplied, "colonyDefaultsApplied", false);
            Scribe_Values.Look(ref policyDefaultsApplied, "policyDefaultsApplied", false);
            Scribe_Values.Look(ref policiesAppliedAtStart, "policiesAppliedAtStart", false);
            Scribe_Values.Look(ref startCard, "startCard", StartNone);
            Scribe_Values.Look(ref startChoice, "startChoice");

            // the per-map pending configs were kept here (keyed by map) until they moved to SmdMapData: read only, never written
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                List<PendingMap> maps = null;
                Scribe_Collections.Look(ref maps, "pending", LookMode.Deep);
                if (Scribe.mode == LoadSaveMode.LoadingVars && maps != null)
                    foreach (var pm in maps)
                        if (pm?.entries != null && pm.entries.Count > 0) legacyPending[pm.map] = pm.entries;
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                UnionInto(seen, seenL); UnionInto(appliedCore, coreL); UnionInto(appliedWork, workL); UnionInto(known, knownL);
            }
        }

        /// <summary>After a load: pending configs from the game node of an early 2.0 save go to their map's data (once).</summary>
        internal static void MoveLegacyPending()
        {
            if (legacyPending.Count == 0) return;
            int moved = 0;
            if (Find.Maps != null)
                foreach (var m in Find.Maps)
                {
                    if (m == null || !legacyPending.TryGetValue(m.uniqueID, out var list) || list == null) continue;
                    list.RemoveAll(e => e == null || e.def == null || e.snapshot == null || e.snapshot.IsEmpty);
                    SmdMapData.For(m).AddRange(list);
                    moved += list.Count;
                }
            legacyPending.Clear();
            if (moved > 0) WBD_Log.Msg("Moved " + moved + " pending blueprint config(s) into the per-map data.");
        }

        /// <summary>Old (1.x / 2.0 wave 2) component data read from a save: merged, never replacing what is already known.</summary>
        internal static void MergeLegacyRoster(List<int> seenL, List<int> coreL, List<int> workL, List<int> knownL, int version, int baseline, bool colonyApplied, bool policyApplied)
        {
            UnionInto(seen, seenL); UnionInto(appliedCore, coreL); UnionInto(appliedWork, workL); UnionInto(known, knownL);
            if (version > rosterVersion) { rosterVersion = version; baselineTick = baseline; }
            colonyDefaultsApplied |= colonyApplied;
            policyDefaultsApplied |= policyApplied;
        }

        static void UnionInto(HashSet<int> set, List<int> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) set.Add(list[i]);
        }

        public sealed class PendingMap : IExposable
        {
            public int map;
            public List<PendingEntry> entries = new List<PendingEntry>();

            public void ExposeData()
            {
                Scribe_Values.Look(ref map, "map", 0);
                Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
                if (Scribe.mode == LoadSaveMode.LoadingVars && entries == null) entries = new List<PendingEntry>();
            }
        }
    }
}
