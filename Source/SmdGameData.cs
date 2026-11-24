using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Everything Set my Defaults keeps per save (2.0): the known colonist roster (M1), the "colony defaults applied" flag (M8) and
    /// the pending blueprint configs per map. Saved through JB Framework's game component (<c>KitRegistry.SaveData</c>), so a save made
    /// with 2.0 holds no class of this mod and loads cleanly after the mod is removed.
    ///
    /// Saves made before 2.0 hold the old <see cref="PawnDefaultsGameComponent"/> and <see cref="WBD_PendingConfigs"/> nodes; those
    /// classes still exist, read their old nodes once and merge them in here (union), and are never written again
    /// (<see cref="Patch_Game_ExposeSmallComponents"/>, <see cref="Patch_Map_ExposeComponents"/>).
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

        // ---- pending blueprint configs, per map (uniqueID) ----
        internal static Dictionary<int, List<PendingEntry>> pending = new Dictionary<int, List<PendingEntry>>();

        public static int KnownCount => known.Count;

        public static List<PendingEntry> PendingFor(Map map)
        {
            if (map == null) return new List<PendingEntry>();
            if (!pending.TryGetValue(map.uniqueID, out var list) || list == null)
                pending[map.uniqueID] = list = new List<PendingEntry>();
            return list;
        }

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
            pending = new Dictionary<int, List<PendingEntry>>();
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

            List<PendingMap> maps = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                maps = new List<PendingMap>();
                var live = new HashSet<int>();
                if (Find.Maps != null) foreach (var m in Find.Maps) if (m != null) live.Add(m.uniqueID);
                foreach (var kv in pending)
                    if (kv.Value != null && kv.Value.Count > 0 && live.Contains(kv.Key))
                        maps.Add(new PendingMap { map = kv.Key, entries = kv.Value });
            }
            Scribe_Collections.Look(ref maps, "pending", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                UnionInto(seen, seenL); UnionInto(appliedCore, coreL); UnionInto(appliedWork, workL); UnionInto(known, knownL);
                if (maps != null)
                    foreach (var pm in maps)
                        if (pm?.entries != null) pending[pm.map] = pm.entries;
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (var kv in pending)
                    kv.Value?.RemoveAll(e => e == null || e.def == null || e.snapshot == null || e.snapshot.IsEmpty);
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

    /// <summary>
    /// Saves made with 2.0 must not contain our old GameComponent class (removing the mod would then log "Could not find class").
    /// While the game writes its components, ours is taken out of the list and put back afterwards. Reading old saves is unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Game), "ExposeSmallComponents")]
    public static class Patch_Game_ExposeSmallComponents
    {
        static void Prefix(Game __instance, out List<GameComponent> __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving || __instance?.components == null) return;
            __state = __instance.components.FindAll(c => c is PawnDefaultsGameComponent);
            if (__state.Count == 0) { __state = null; return; }
            __instance.components.RemoveAll(c => c is PawnDefaultsGameComponent);
        }

        static void Finalizer(Game __instance, List<GameComponent> __state)
        {
            // Game.ExposeSmallComponents fills missing components only while loading; drop any fresh copy anyway.
            if (__state == null || __instance?.components == null) return;
            __instance.components.RemoveAll(c => c is PawnDefaultsGameComponent && !__state.Contains(c));
            __instance.components.AddRange(__state);
        }
    }

    /// <summary>The same for the old per-map component (pending blueprint configs now live in <see cref="SmdGameData"/>).</summary>
    [HarmonyPatch(typeof(Map), "ExposeComponents")]
    public static class Patch_Map_ExposeComponents
    {
        static void Prefix(Map __instance, out List<MapComponent> __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving || __instance?.components == null) return;
            __state = __instance.components.FindAll(c => c is WBD_PendingConfigs);
            if (__state.Count == 0) { __state = null; return; }
            __instance.components.RemoveAll(c => c is WBD_PendingConfigs);
        }

        static void Finalizer(Map __instance, List<MapComponent> __state)
        {
            // Map.ExposeComponents calls FillComponents in every mode, so while saving it made a fresh copy of the component we took
            // out: drop that copy, put ours back (found by kit.saveload: two components after a save).
            if (__state == null || __instance?.components == null) return;
            __instance.components.RemoveAll(c => c is WBD_PendingConfigs && !__state.Contains(c));
            __instance.components.AddRange(__state);
        }
    }
}
