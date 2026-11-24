using System;
using BetterWorkbenchDefaults.Lib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The colonist presets (one default + extra named setups). Since 2.0 they live in the cross-save library
    /// (section "pawns"); this class keeps the 1.x API and converts the stored XML into live
    /// <see cref="PawnConfigSnapshot"/>s on demand (cached per library revision).
    /// </summary>
    public static class PawnDefaultsStore
    {
        private static int _rev = -1;
        private static PawnPresets _cache;

        public static PresetSet GetStored() => SmdLibraryStore.Content.pawns;

        public static PawnPresets GetPresets()
        {
            int rev = SmdLibraryStore.Revision;
            if (rev == _rev)
                return _cache;
            var stored = GetStored();
            if (stored == null || stored.IsEmpty)
            {
                _cache = null;
                _rev = rev;
                return null;
            }
            var live = new PawnPresets { defaultName = stored.DefaultPreset?.name };
            foreach (var sp in stored.presets)
            {
                var snap = new PawnConfigSnapshot();
                if (!SmdScribe.Read(sp.config, snap.ExposeData))
                {
                    if (SmdScribe.Busy)
                        return null; // game saving/loading: try again later, do not cache
                    snap = new PawnConfigSnapshot();
                }
                live.presets.Add(new PawnPreset(sp.name, snap));
            }
            _cache = live;
            _rev = rev;
            return live;
        }

        public static PawnConfigSnapshot GetDefaultConfig() => GetPresets()?.DefaultConfig;

        public static bool HasPresets()
        {
            var s = GetStored();
            return s != null && !s.IsEmpty;
        }

        public static bool SavePreset(string name, PawnConfigSnapshot config, bool makeDefault)
        {
            if (config == null || config.IsEmpty || name.NullOrEmpty())
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
                if (lib.content.pawns == null)
                    lib.content.pawns = new PresetSet { key = "pawns" };
                lib.content.pawns.AddOrReplace(stored, makeDefault);
                return true;
            });
        }

        public static bool RemovePreset(string name) =>
            SmdLibraryStore.Mutate(lib => lib.content.pawns?.Remove(name) ?? false);

        public static bool SetDefaultPreset(string name) =>
            SmdLibraryStore.Mutate(lib => lib.content.pawns?.SetDefault(name) ?? false);

        public static bool RenamePreset(string oldName, string newName) =>
            SmdLibraryStore.Mutate(lib => lib.content.pawns?.Rename(oldName, newName) ?? false);

        public static void ClearAll() =>
            SmdLibraryStore.Mutate(lib =>
            {
                if (lib.content.pawns == null)
                    return false;
                lib.content.pawns = null;
                return true;
            });
    }
}
