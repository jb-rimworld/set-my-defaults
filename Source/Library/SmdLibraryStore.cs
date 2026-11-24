using System;
using System.IO;
using BetterWorkbenchDefaults.Lib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>Where the library lives. Tests redirect it with the environment variable SMD_LIBRARY_DIR.</summary>
    public static class SmdPaths
    {
        private static string _configRoot;
        private static string _libraryFolder;

        /// <summary>The folder that holds RimWorld's Config folder (the 1.x preset folders live here too).</summary>
        public static string ConfigRoot
        {
            get
            {
                if (_configRoot == null)
                    _configRoot = Directory.GetParent(GenFilePaths.ConfigFolderPath)?.FullName ?? GenFilePaths.ConfigFolderPath;
                return _configRoot;
            }
        }

        public static string LibraryFolder
        {
            get
            {
                if (_libraryFolder == null)
                {
                    var over = Environment.GetEnvironmentVariable("SMD_LIBRARY_DIR");
                    _libraryFolder = !string.IsNullOrEmpty(over) ? over : Path.Combine(ConfigRoot, "SetMyDefaults");
                }
                return _libraryFolder;
            }
        }

        public static string LibraryFile => Path.Combine(LibraryFolder, Lib.LibraryFile.FileName);

        /// <summary>Where the 1.x files are read from for migration (the test override has its own copy).</summary>
        public static string LegacyRoot
        {
            get
            {
                var over = Environment.GetEnvironmentVariable("SMD_LEGACY_DIR");
                return !string.IsNullOrEmpty(over) ? over : ConfigRoot;
            }
        }
    }

    /// <summary>
    /// The one cross-save library (M4/M7). Loaded lazily, migrated from 1.x files the first time, edited by
    /// clone-then-commit: a change becomes visible only after the file write succeeded. If the file exists but cannot
    /// be read, the store is read-only for the session and nothing is written (the player's file is never replaced
    /// by an empty one).
    /// </summary>
    public static class SmdLibraryStore
    {
        private static SmdLibrary _lib;
        private static bool _loaded;

        /// <summary>Incremented after every successful change; caches of converted templates compare against it.</summary>
        public static int Revision { get; private set; }

        /// <summary>Set when Library.xml exists but could not be read (the reason). Writes are refused while set.</summary>
        public static string LoadError { get; private set; }

        public static bool ReadOnly => LoadError != null;

        /// <summary>Result of the automatic 1.x import on first start (null when none ran this session).</summary>
        public static LegacyMigration.Result LastMigration { get; private set; }

        public static SmdLibrary Library
        {
            get
            {
                EnsureLoaded();
                return _lib;
            }
        }

        public static SmdContent Content => Library.content;

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            _lib = new SmdLibrary();
            try
            {
                SmdScribe.CleanTemp();
                var r = LibraryFile.Load(SmdPaths.LibraryFile);
                if (r.error != null)
                {
                    LoadError = r.error;
                    WBD_Log.Error("Library.xml could not be read and is left untouched (" + r.error + "). Presets are read-only this session; fix or remove the file, the previous version is Library.xml.bak.");
                    return;
                }
                if (r.library != null)
                {
                    _lib = r.library;
                    return;
                }
                // First start of 2.0: import the 1.x files once (they stay where they are).
                if (LegacyMigration.HasLegacyFiles(SmdPaths.LegacyRoot))
                {
                    var lib = new SmdLibrary();
                    var m = LegacyMigration.ImportAll(SmdPaths.LegacyRoot, lib, overwrite: false);
                    LastMigration = m;
                    LibraryFile.Save(SmdPaths.LibraryFile, lib);
                    _lib = lib;
                    WBD_Log.Msg("Imported presets from the previous version: " + m + ". The old files were not changed.");
                }
            }
            catch (Exception e)
            {
                LoadError = e.GetType().Name + ": " + e.Message;
                WBD_Log.Error("Library could not be prepared: " + e);
            }
            finally
            {
                Revision++;
            }
        }

        /// <summary>
        /// Change the library: <paramref name="edit"/> works on a CLONE and returns false to abort. The clone replaces
        /// the live library only after it was written to disk. Returns false when nothing was saved.
        /// </summary>
        public static bool Mutate(Func<SmdLibrary, bool> edit)
        {
            EnsureLoaded();
            if (ReadOnly)
            {
                WBD_Log.Warn("Library is read-only this session: " + LoadError);
                return false;
            }
            try
            {
                var working = _lib.Clone();
                if (!edit(working))
                    return false;
                working.content.RemoveEmpty();
                LibraryFile.Save(SmdPaths.LibraryFile, working);
                _lib = working;
                Revision++;
                return true;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Could not save the library: " + e);
                return false;
            }
        }

        /// <summary>
        /// Import the 1.x files again (for players who kept saving presets with the 1.6 version). Existing presets with
        /// the same name are kept unless <paramref name="overwrite"/>. Returns null when the library is read-only.
        /// </summary>
        public static LegacyMigration.Result ReimportLegacy(bool overwrite)
        {
            LegacyMigration.Result result = null;
            Mutate(lib =>
            {
                result = LegacyMigration.ImportAll(SmdPaths.LegacyRoot, lib, overwrite);
                return result.AnythingFound;
            });
            return result;
        }

        /// <summary>Drop the cached library so the next access reads the file again (tests, "reload" button).</summary>
        public static void Reload()
        {
            _loaded = false;
            _lib = null;
            LoadError = null;
            EnsureLoaded();
        }
    }
}
