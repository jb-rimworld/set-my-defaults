using System;
using System.Collections.Generic;
using System.Linq;
using BetterWorkbenchDefaults.Lib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Building and zone presets, keyed by building defName (or "zone:stockpile", "zone:dumping", "zone:growing").
    /// Since 2.0 the data lives in the cross-save library (<see cref="SmdLibraryStore"/>); this class keeps the 1.x
    /// API and hands out LIVE templates (<see cref="WorkbenchPresets"/>) converted from the stored XML on demand.
    ///
    /// Nothing is pruned on load any more (fixes B4): a bill whose recipe belongs to a mod that is off right now is
    /// simply not instantiated, while the stored preset keeps it. Rename, delete and set-default touch only the names,
    /// never the stored content of other presets.
    /// </summary>
    public static class DefaultBillsDatabase
    {
        public const string ZoneStockpile = "zone:stockpile";
        public const string ZoneDumping = "zone:dumping";
        public const string ZoneGrowing = "zone:growing";

        public static bool IsZoneKey(string key) => SmdContent.IsZoneKey(key);

        private struct Cached
        {
            public int revision;
            public WorkbenchPresets presets;
        }

        private static readonly Dictionary<string, Cached> _cache = new Dictionary<string, Cached>();

        // ---- queries ----

        public static PresetSet GetStored(string key)
        {
            if (key == null)
                return null;
            return SmdLibraryStore.Content.Buildings(key);
        }

        public static WorkbenchPresets GetPresets(string key)
        {
            if (key == null)
                return null;
            int rev = SmdLibraryStore.Revision;
            if (_cache.TryGetValue(key, out var c) && c.revision == rev)
                return c.presets;
            var stored = GetStored(key);
            if (stored == null || stored.IsEmpty)
            {
                _cache[key] = new Cached { revision = rev, presets = null };
                return null;
            }
            var live = new WorkbenchPresets { defaultName = stored.DefaultPreset?.name };
            foreach (var sp in stored.presets)
            {
                var snap = new ConfigSnapshot();
                if (!SmdScribe.Read(sp.config, snap.ExposeData))
                {
                    if (SmdScribe.Busy)
                        return null; // try again when the game is not saving/loading; do not cache
                    snap = new ConfigSnapshot(); // unreadable: keep the name so the player can see and delete it
                }
                live.presets.Add(new WorkbenchPreset(sp.name, snap));
            }
            _cache[key] = new Cached { revision = rev, presets = live };
            return live;
        }

        public static WorkbenchPresets GetPresets(ThingDef def) => GetPresets(def?.defName);

        /// <summary>The default config of exactly this key (no family fallback).</summary>
        public static ConfigSnapshot GetOwnDefaultConfig(string key) => GetPresets(key)?.DefaultConfig;

        /// <summary>
        /// The default config for a building: its own default preset, else (bench families on) the default of the
        /// closest related building (<see cref="BenchFamilies"/>).
        /// </summary>
        public static ConfigSnapshot GetDefaultConfig(ThingDef def)
        {
            if (def == null)
                return null;
            var own = GetOwnDefaultConfig(def.defName);
            if (own != null && !own.IsEmpty)
                return own;
            var famKey = BenchFamilies.FallbackKeyFor(def);
            return famKey != null ? GetOwnDefaultConfig(famKey) : own;
        }

        public static bool HasPresets(ThingDef def) => HasPresets(def?.defName);

        public static bool HasPresets(string key)
        {
            var s = GetStored(key);
            return s != null && !s.IsEmpty;
        }

        /// <summary>All keys with saved presets (building defNames and zone keys).</summary>
        public static List<string> AllSavedDefNames() =>
            SmdLibraryStore.Content.buildings.Where(b => !b.IsEmpty).Select(b => b.key).ToList();

        // ---- mutations ----

        public static bool SavePreset(ThingDef def, string name, ConfigSnapshot config, bool makeDefault) =>
            def != null && SavePreset(def.defName, name, config, makeDefault);

        public static bool SavePreset(string key, string name, ConfigSnapshot config, bool makeDefault)
        {
            if (key == null || config == null || config.IsEmpty || name.NullOrEmpty())
                return false;
            var xml = SmdScribe.Write(config.ExposeData);
            if (xml == null)
                return false;
            var stored = new StoredPreset
            {
                name = name.Trim(),
                config = xml,
                requiredMods = SmdDefRefs.RequiredMods(xml),
                savedAt = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };
            return SmdLibraryStore.Mutate(lib =>
            {
                lib.content.GetOrAddBuildings(key).AddOrReplace(stored, makeDefault);
                return true;
            });
        }

        public static bool RemovePreset(string key, string name) =>
            SmdLibraryStore.Mutate(lib => lib.content.Buildings(key)?.Remove(name) ?? false);

        public static bool SetDefaultPreset(string key, string name) =>
            SmdLibraryStore.Mutate(lib => lib.content.Buildings(key)?.SetDefault(name) ?? false);

        public static bool RenamePreset(string key, string oldName, string newName) =>
            SmdLibraryStore.Mutate(lib => lib.content.Buildings(key)?.Rename(oldName, newName) ?? false);

        public static void ClearAll(ThingDef def)
        {
            if (def != null)
                ClearAll(def.defName);
        }

        public static void ClearAll(string key)
        {
            if (key == null)
                return;
            SmdLibraryStore.Mutate(lib => lib.content.buildings.RemoveAll(b => b.key == key) > 0);
        }

        /// <summary>A readable label for a key ("Electric stove", "Stockpile zones", or the raw defName of a missing mod).</summary>
        public static string KeyLabel(string key)
        {
            switch (key)
            {
                case ZoneStockpile: return "WBD_KeyZoneStockpile".Translate();
                case ZoneDumping: return "WBD_KeyZoneDumping".Translate();
                case ZoneGrowing: return "WBD_KeyZoneGrowing".Translate();
            }
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(key);
            return def != null ? def.LabelCap.ToString() : "WBD_MissingDef".Translate(key).ToString();
        }
    }
}
