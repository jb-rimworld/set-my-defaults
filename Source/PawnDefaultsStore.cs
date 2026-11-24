using System;
using System.IO;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Global, on-disk store of pawn presets (one default + extra named setups). Unlike the
    /// per-building-type <see cref="DefaultBillsDatabase"/>, pawns share a SINGLE global set, so this
    /// is one side-file next to RimWorld's Config folder. Lazy-loaded + cached. Reuses the standalone
    /// Scribe save/load discipline (mode guards + ForceStop on error) and the edit-a-clone-then-commit
    /// pattern so a failed disk write never leaves the cache out of sync with disk.
    /// </summary>
    public static class PawnDefaultsStore
    {
        private static PawnPresets _cache;
        private static bool _loaded;
        private static string _folderPath;

        private static string FolderPath
        {
            get
            {
                if (_folderPath == null)
                {
                    var configRoot = Directory.GetParent(GenFilePaths.ConfigFolderPath)?.FullName
                                     ?? GenFilePaths.ConfigFolderPath;
                    _folderPath = Path.Combine(configRoot, "PawnDefaults");
                }
                return _folderPath;
            }
        }

        private static string FilePath => Path.Combine(FolderPath, "Pawns.xml");

        // ---- queries ----

        public static PawnPresets GetPresets()
        {
            if (_loaded)
                return _cache;
            var loaded = LoadFromDisk(out bool definitive);
            if (definitive)
            {
                _cache = loaded;
                _loaded = true;
            }
            return loaded;
        }

        public static PawnConfigSnapshot GetDefaultConfig() => GetPresets()?.DefaultConfig;

        public static bool HasPresets()
        {
            var p = GetPresets();
            return p != null && !p.IsEmpty;
        }

        // ---- mutations (load -> clone -> modify -> commit) ----

        public static bool SavePreset(string name, PawnConfigSnapshot config, bool makeDefault)
        {
            if (config == null || config.IsEmpty || name.NullOrEmpty())
                return false;
            var working = GetPresets()?.Clone() ?? new PawnPresets();
            working.AddOrReplace(name, config, makeDefault);
            return Commit(working);
        }

        public static bool RemovePreset(string name)
        {
            var working = GetPresets()?.Clone();
            if (working == null || !working.Remove(name))
                return false;
            return Commit(working);
        }

        public static bool SetDefaultPreset(string name)
        {
            var working = GetPresets()?.Clone();
            if (working == null || !working.SetDefault(name))
                return false;
            return Commit(working);
        }

        public static bool RenamePreset(string oldName, string newName)
        {
            var working = GetPresets()?.Clone();
            if (working == null || !working.Rename(oldName, newName))
                return false;
            return Commit(working);
        }

        public static void ClearAll()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                _cache = null;
                _loaded = true;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to delete pawn presets file: " + e);
            }
        }

        /// <summary>Persist the preset set and update the cache. Returns false if the write failed.</summary>
        private static bool Commit(PawnPresets presets)
        {
            presets?.NormalizeDefault();
            var toSave = (presets != null && !presets.IsEmpty) ? presets : null;
            if (!SaveToDisk(toSave))
                return false;
            _cache = toSave;
            _loaded = true;
            return true;
        }

        // ---- disk ----

        private static bool SaveToDisk(PawnPresets presets)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                WBD_Log.Warn("Could not save pawn presets right now (game is saving/loading). Try again in a moment.");
                return false;
            }
            try
            {
                if (presets == null || presets.IsEmpty)
                {
                    if (File.Exists(FilePath))
                        File.Delete(FilePath);
                    return true;
                }
                Directory.CreateDirectory(FolderPath);
                Scribe.saver.InitSaving(FilePath, "pawnDefaults");
                try
                {
                    var version = 1;
                    Scribe_Values.Look(ref version, "version", 0);
                    presets.ExposeData();
                }
                finally
                {
                    Scribe.saver.FinalizeSaving();
                }
                return true;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to save pawn presets: " + e);
                Scribe.ForceStop();
                return false;
            }
        }

        private static PawnPresets LoadFromDisk(out bool definitive)
        {
            definitive = true;
            try
            {
                if (!File.Exists(FilePath))
                    return null;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to resolve pawn presets path: " + e);
                return null;
            }

            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                WBD_Log.Warn("Could not load pawn presets right now (game is saving/loading).");
                definitive = false;
                return null;
            }

            var presets = new PawnPresets();
            try
            {
                Scribe.loader.InitLoading(FilePath);
                try
                {
                    var version = 0;
                    Scribe_Values.Look(ref version, "version", 0);
                    presets.ExposeData();
                }
                finally
                {
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to load pawn presets: " + e);
                Scribe.ForceStop();
                return null;
            }

            if (!presets.IsEmpty)
            {
                presets.presets.RemoveAll(p => p == null || p.config == null || p.config.IsEmpty);
                presets.NormalizeDefault();
            }

            return presets.IsEmpty ? null : presets;
        }
    }
}
