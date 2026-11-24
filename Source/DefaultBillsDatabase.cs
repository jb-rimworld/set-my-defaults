using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Global, on-disk store of per-building-type presets, keyed by building defName. Each def
    /// has a <see cref="WorkbenchPresets"/> set (one default + extra named setups). Files live in
    /// a folder next to RimWorld's Config folder, so presets are shared across colonies and
    /// survive save/quit. Lazy-loaded + cached; backward compatible with the original single
    /// (unnamed) default format (migrated to one preset named "Default").
    /// </summary>
    public static class DefaultBillsDatabase
    {
        private static readonly Dictionary<string, WorkbenchPresets> _cache =
            new Dictionary<string, WorkbenchPresets>();

        // Set of defNames that have a saved file (for the mod-settings overview).
        private static HashSet<string> _known;

        private static string _folderPath;

        private static string FolderPath
        {
            get
            {
                if (_folderPath == null)
                {
                    var configRoot = Directory.GetParent(GenFilePaths.ConfigFolderPath)?.FullName
                                     ?? GenFilePaths.ConfigFolderPath;
                    _folderPath = Path.Combine(configRoot, "WorkbenchBillDefaults");
                }
                return _folderPath;
            }
        }

        private static string FilePathFor(string defName) => Path.Combine(FolderPath, Sanitize(defName) + ".xml");

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static void EnsureScanned()
        {
            if (_known != null)
                return;
            _known = new HashSet<string>();
            try
            {
                if (Directory.Exists(FolderPath))
                    foreach (var f in Directory.GetFiles(FolderPath, "*.xml"))
                        _known.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to scan defaults folder: " + e);
            }
        }

        // ---- queries ----

        public static WorkbenchPresets GetPresets(string defName)
        {
            if (defName == null)
                return null;
            if (_cache.TryGetValue(defName, out var cached))
                return cached;
            var loaded = LoadFromDisk(defName, out bool definitive);
            if (definitive)
                _cache[defName] = loaded;
            return loaded;
        }

        public static WorkbenchPresets GetPresets(ThingDef def) => GetPresets(def?.defName);

        public static ConfigSnapshot GetDefaultConfig(ThingDef def) => GetPresets(def)?.DefaultConfig;

        public static bool HasPresets(ThingDef def)
        {
            var p = GetPresets(def);
            return p != null && !p.IsEmpty;
        }

        public static List<string> AllSavedDefNames()
        {
            EnsureScanned();
            return _known.ToList();
        }

        // ---- mutations (load -> modify -> save) ----

        // All mutators edit a CLONE and only swap it into the cache if the disk write succeeds
        // (Commit does that), so a failed save never leaves the cache out of sync with disk.

        public static bool SavePreset(ThingDef def, string name, ConfigSnapshot config, bool makeDefault)
        {
            if (def == null || config == null || config.IsEmpty || name.NullOrEmpty())
                return false;
            var working = GetPresets(def.defName)?.Clone() ?? new WorkbenchPresets();
            working.AddOrReplace(name, config, makeDefault);
            return Commit(def.defName, working);
        }

        public static bool RemovePreset(string defName, string name)
        {
            var working = GetPresets(defName)?.Clone();
            if (working == null || !working.Remove(name))
                return false;
            return Commit(defName, working);
        }

        public static bool SetDefaultPreset(string defName, string name)
        {
            var working = GetPresets(defName)?.Clone();
            if (working == null || !working.SetDefault(name))
                return false;
            return Commit(defName, working);
        }

        public static bool RenamePreset(string defName, string oldName, string newName)
        {
            var working = GetPresets(defName)?.Clone();
            if (working == null || !working.Rename(oldName, newName))
                return false;
            return Commit(defName, working);
        }

        public static void ClearAll(ThingDef def)
        {
            if (def != null)
                ClearAll(def.defName);
        }

        public static void ClearAll(string defName)
        {
            if (defName == null)
                return;
            try
            {
                var path = FilePathFor(defName);
                if (File.Exists(path))
                    File.Delete(path);
                // Update caches only after the delete actually succeeded.
                _cache[defName] = null;
                _known?.Remove(defName);
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to delete presets file for " + defName + ": " + e);
            }
        }

        /// <summary>Persist a preset set and update caches. Returns false if the write failed.</summary>
        private static bool Commit(string defName, WorkbenchPresets presets)
        {
            presets?.NormalizeDefault();
            var toSave = (presets != null && !presets.IsEmpty) ? presets : null;
            if (!SaveToDisk(defName, toSave))
                return false;
            _cache[defName] = toSave;
            if (_known != null)
            {
                if (toSave != null)
                    _known.Add(defName);
                else
                    _known.Remove(defName);
            }
            return true;
        }

        // ---- disk ----

        private static bool SaveToDisk(string defName, WorkbenchPresets presets)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                WBD_Log.Warn("Could not save presets right now (game is saving/loading). Try again in a moment.");
                return false;
            }
            try
            {
                var path = FilePathFor(defName);
                if (presets == null || presets.IsEmpty)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    return true;
                }
                Directory.CreateDirectory(FolderPath);
                Scribe.saver.InitSaving(path, "workbenchBillDefaults");
                try
                {
                    var version = 3;
                    Scribe_Values.Look(ref version, "version", 0); // default 0 != 3 so it's written
                    var workbench = defName;
                    Scribe_Values.Look(ref workbench, "workbench");
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
                WBD_Log.Error("Failed to save presets for " + defName + ": " + e);
                Scribe.ForceStop();
                return false;
            }
        }

        private static WorkbenchPresets LoadFromDisk(string defName, out bool definitive)
        {
            definitive = true;
            string path;
            try
            {
                path = FilePathFor(defName);
                if (!File.Exists(path))
                    return null;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to resolve presets path for " + defName + ": " + e);
                return null;
            }

            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                WBD_Log.Warn("Could not load presets right now (game is saving/loading).");
                definitive = false;
                return null;
            }

            var presets = new WorkbenchPresets();
            var legacy = new ConfigSnapshot(); // for migrating the original single-default format
            try
            {
                Scribe.loader.InitLoading(path);
                try
                {
                    var version = 0;
                    Scribe_Values.Look(ref version, "version", 0);
                    var workbench = "";
                    Scribe_Values.Look(ref workbench, "workbench");
                    presets.ExposeData();          // v3 (named presets)
                    legacy.ExposeData();           // v2 (root bills/storage/splitter), absent in v3
                }
                finally
                {
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to load presets for " + defName + ": " + e);
                Scribe.ForceStop();
                return null;
            }

            // Migrate a legacy single-config file into one "Default" preset.
            if (presets.IsEmpty && !legacy.IsEmpty)
                presets = WorkbenchPresets.FromLegacy(legacy);

            // Prune bills whose recipe no longer exists, then drop presets that became empty.
            if (!presets.IsEmpty)
            {
                presets.presets.RemoveAll(p => p == null);
                foreach (var preset in presets.presets)
                    preset.config?.bills?.RemoveAll(t => t == null || !t.IsValid);
                presets.presets.RemoveAll(p => p.config == null || p.config.IsEmpty);
                presets.NormalizeDefault();
            }

            return presets.IsEmpty ? null : presets;
        }
    }
}
