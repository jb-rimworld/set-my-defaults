using System;
using System.IO;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Global, on-disk store of the cross-save "default policies" set (one <see cref="PolicyDefaultsSet"/>),
    /// modeled exactly on <see cref="PawnDefaultsStore"/>: a single side-file next to RimWorld's Config
    /// folder, lazy-loaded + cached, written with the standalone Scribe InitSaving/FinalizeSaving
    /// discipline (mode guards + ForceStop on error) and an edit-a-clone-then-commit pattern so a failed
    /// disk write never desyncs the cache from disk.
    ///
    /// The file lives inside the existing <c>PawnDefaults</c> folder as <c>PolicyDefaults.xml</c> (policies
    /// are pawn-adjacent; the store owns its own filename/version).
    /// </summary>
    public static class PolicyDefaultsStore
    {
        private static PolicyDefaultsSet _cache;
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

        private static string FilePath => Path.Combine(FolderPath, "PolicyDefaults.xml");

        // ---- queries ----

        public static PolicyDefaultsSet GetSet()
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

        public static bool HasSet()
        {
            var s = GetSet();
            return s != null && !s.IsEmpty;
        }

        // ---- player actions ----

        /// <summary>
        /// Capture the current colony's four policy databases into a fresh set and persist it
        /// cross-save. Requires a loaded game and an idle Scribe. Returns false if it could not run.
        /// </summary>
        public static bool SaveCurrentColony()
        {
            if (Current.Game == null || Scribe.mode != LoadSaveMode.Inactive)
                return false;

            var set = PolicyDefaultsController.CaptureCurrentColony();
            if (set == null || set.IsEmpty)
                return false;
            return Commit(set);
        }

        /// <summary>
        /// Apply the saved set into the current colony: recreate-by-label each policy into the matching
        /// database, then mark the saved default as the colony default (database index 0). Idempotent.
        /// Returns false if there is nothing to apply or the game is busy/unloaded.
        /// </summary>
        public static bool ApplyToCurrentColony()
        {
            if (Current.Game == null || Scribe.mode != LoadSaveMode.Inactive)
                return false;
            var set = GetSet();
            if (set == null || set.IsEmpty)
                return false;
            return PolicyDefaultsController.ApplySetToColony(set);
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
                WBD_Log.Error("Failed to delete policy defaults file: " + e);
            }
        }

        /// <summary>Persist the set and update the cache. Returns false if the write failed.</summary>
        private static bool Commit(PolicyDefaultsSet set)
        {
            set?.NormalizeDefault();
            var toSave = (set != null && !set.IsEmpty) ? set : null;
            if (!SaveToDisk(toSave))
                return false;
            _cache = toSave;
            _loaded = true;
            return true;
        }

        // ---- disk ----

        private static bool SaveToDisk(PolicyDefaultsSet set)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                WBD_Log.Warn("Could not save policy defaults right now (game is saving/loading). Try again in a moment.");
                return false;
            }
            try
            {
                if (set == null || set.IsEmpty)
                {
                    if (File.Exists(FilePath))
                        File.Delete(FilePath);
                    return true;
                }
                Directory.CreateDirectory(FolderPath);
                Scribe.saver.InitSaving(FilePath, "policyDefaults");
                try
                {
                    var version = 1;
                    Scribe_Values.Look(ref version, "version", 0);
                    set.ExposeData();
                }
                finally
                {
                    Scribe.saver.FinalizeSaving();
                }
                return true;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to save policy defaults: " + e);
                Scribe.ForceStop();
                return false;
            }
        }

        private static PolicyDefaultsSet LoadFromDisk(out bool definitive)
        {
            definitive = true;
            try
            {
                if (!File.Exists(FilePath))
                    return null;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to resolve policy defaults path: " + e);
                return null;
            }

            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                WBD_Log.Warn("Could not load policy defaults right now (game is saving/loading).");
                definitive = false;
                return null;
            }

            var set = new PolicyDefaultsSet();
            try
            {
                Scribe.loader.InitLoading(FilePath);
                try
                {
                    var version = 0;
                    Scribe_Values.Look(ref version, "version", 0);
                    set.ExposeData();
                }
                finally
                {
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to load policy defaults: " + e);
                Scribe.ForceStop();
                return null;
            }

            if (!set.IsEmpty)
                set.NormalizeDefault();

            return set.IsEmpty ? null : set;
        }
    }
}
