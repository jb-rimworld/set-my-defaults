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
    /// The one cross-save library (M4/M7). Loaded lazily, migrated from 1.x files the first time, edited by clone-then-commit: a change
    /// becomes visible only after the file write succeeded. Every write goes through <see cref="LibraryDisk"/>: when another game (a
    /// second RimWorld) changed the file meanwhile, the change is made on that newer version, so neither game's presets are lost; the
    /// library is also reloaded by itself (checked every few seconds) when the file changed on disk. If the file exists but cannot be
    /// read, the store is read-only for the session and nothing is written (the player's file is never replaced); the session then
    /// uses Library.xml.bak (read-only) and the library page offers "Restore the backup".
    /// </summary>
    public static class SmdLibraryStore
    {
        private static SmdLibrary _lib;
        private static bool _loaded;
        private static LibraryDisk _disk;
        private static int _lastDiskCheck;
        /// <summary>How often (ms) a getter looks whether the file changed on disk (another game wrote it).</summary>
        public const int DiskCheckMs = 3000;

        /// <summary>Incremented after every change of the in-memory library; caches of converted templates compare against it.</summary>
        public static int Revision { get; private set; }

        /// <summary>Set when Library.xml exists but could not be read (the reason). Writes are refused while set.</summary>
        public static string LoadError { get; private set; }

        public static bool ReadOnly => LoadError != null;

        /// <summary>While read-only: the session uses the readable Library.xml.bak (so colonies still get their defaults).</summary>
        public static bool UsingBackup { get; private set; }

        /// <summary>Why the last change could not be saved (null after a successful one).</summary>
        public static string LastSaveError { get; private set; }

        // kinds and technical details of the two errors, for the player's language (LoadError/LastSaveError stay English for logs)
        static LibErrorKind loadKind, saveKind;
        static string loadDetail, saveDetail;

        /// <summary><see cref="LoadError"/> in the player's language (null when none).</summary>
        public static string LoadErrorText => LoadError == null ? null : SmdFileText.Explain(loadKind, loadDetail, LoadError);

        /// <summary><see cref="LastSaveError"/> in the player's language (null when none).</summary>
        public static string LastSaveErrorText => LastSaveError == null ? null : SmdFileText.Explain(saveKind, saveDetail, LastSaveError);

        static void SetLoadError(string english, LibErrorKind kind, string detail) { LoadError = english; loadKind = kind; loadDetail = detail; }
        static void SetSaveError(string english, LibErrorKind kind, string detail) { LastSaveError = english; saveKind = kind; saveDetail = detail; }

        /// <summary>A file of a newer format version was read (its number), 0 otherwise.</summary>
        public static int NewerFormat { get; private set; }

        /// <summary>Result of the automatic 1.x import on first start (null when none ran this session).</summary>
        public static LegacyMigration.Result LastMigration { get; private set; }

        public static SmdLibrary Library
        {
            get
            {
                EnsureLoaded();
                RefreshFromDisk(false);
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
            UsingBackup = false;
            _lastDiskCheck = Environment.TickCount;
            try
            {
                SmdScribe.CleanTemp();
                _disk = new LibraryDisk(SmdPaths.LibraryFile);
                LibraryFile.CleanStaleTemps(SmdPaths.LibraryFile);
                var r = _disk.Load();
                if (r.error != null)
                {
                    GoReadOnly(r.error, LibErrorKind.Unreadable, r.detail);
                    return;
                }
                if (r.library != null)
                {
                    _lib = r.library;
                    NoteVersion(_lib);
                    return;
                }
                // First start of 2.0: import the 1.x files once (they stay where they are). Through the disk lock: when another game
                // creates the library at the same moment, the import is added to that one.
                if (LegacyMigration.HasLegacyFiles(SmdPaths.LegacyRoot))
                {
                    LegacyMigration.Result m = null;
                    var c = _disk.Commit(_lib, lib =>
                    {
                        m = LegacyMigration.ImportAll(SmdPaths.LegacyRoot, lib, overwrite: false);
                        return true;
                    });
                    if (c.library != null)
                        _lib = c.library;
                    LastMigration = m;
                    if (c.saved)
                        WBD_Log.Msg("Imported presets from the previous version: " + m + ". The old files were not changed.");
                    else
                        WBD_Log.Warn("Presets of the previous version could not be written to Library.xml (" + c.error + "); they are used this session and imported again next time. The old files were not changed.");
                }
            }
            catch (Exception e)
            {
                GoReadOnly(e.GetType().Name + ": " + e.Message, LibErrorKind.Unreadable, e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                Revision++;
            }
        }

        /// <summary>The file cannot be read: never write it, explain it, and use the backup (read-only) when that one is readable.</summary>
        private static void GoReadOnly(string error, LibErrorKind kind, string detail)
        {
            SetLoadError(error, kind, detail);
            var bak = LibraryFile.Load(SmdPaths.LibraryFile + LibraryFile.BackupSuffix);
            if (bak.library != null)
            {
                _lib = bak.library;
                UsingBackup = true;
            }
            WBD_Log.Warn("Library.xml could not be read and is left untouched (" + error + "). Presets are read-only this session"
                + (UsingBackup ? "; the previous version (Library.xml.bak) is used meanwhile" : "")
                + ". Open the library page to restore the backup, or fix the file.");
        }

        private static void NoteVersion(SmdLibrary lib)
        {
            if (lib.FileVersion > SmdLibrary.CurrentVersion && NewerFormat != lib.FileVersion)
            {
                NewerFormat = lib.FileVersion;
                WBD_Log.Msg("Library.xml was written by a newer version of Set my Defaults (format " + lib.FileVersion + ", this version knows " + SmdLibrary.CurrentVersion
                    + "). Everything this version does not know is kept as it is; before the first change a copy is saved as Library.format" + lib.FileVersion + ".xml.");
            }
        }

        /// <summary>
        /// Reload when the file changed on disk (another game or a hand edit). Throttled to every <see cref="DiskCheckMs"/> unless
        /// <paramref name="force"/>. A changed file that cannot be read makes the store read-only (the in-memory copy stays in use).
        /// </summary>
        public static void RefreshFromDisk(bool force)
        {
            if (!_loaded || _disk == null || ReadOnly)
                return;
            int now = Environment.TickCount;
            if (!force && unchecked(now - _lastDiskCheck) < DiskCheckMs)
                return;
            _lastDiskCheck = now;
            try
            {
                if (!_disk.ChangedOnDisk())
                    return;
                var r = _disk.Load();
                if (r.library != null)
                {
                    _lib = r.library;
                    NoteVersion(_lib);
                    Revision++;
                    WBD_Log.Msg("Library.xml was changed outside this game (another game or a hand edit) and was read again.");
                }
                else if (r.error != null)
                {
                    SetLoadError(r.error, LibErrorKind.ChangedUnreadable, r.detail);
                    Revision++;
                    WBD_Log.Warn("Library.xml was changed outside this game and cannot be read (" + r.error + "). It is left untouched; presets are read-only until it is fixed or the backup is restored.");
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not check Library.xml for changes: " + e.Message);
            }
        }

        /// <summary>
        /// Change the library: <paramref name="edit"/> works on a CLONE of the newest version and returns false to abort. The clone
        /// replaces the live library only after it was written to disk. Returns false when nothing was saved
        /// (<see cref="LastSaveError"/> says why when it was not the edit's own choice).
        /// </summary>
        public static bool Mutate(Func<SmdLibrary, bool> edit)
        {
            EnsureLoaded();
            if (ReadOnly)
            {
                SetSaveError(LoadError, loadKind, loadDetail);
                WBD_Log.Warn("Library is read-only this session: " + LoadError);
                return false;
            }
            LastSaveError = null;
            try
            {
                var r = _disk.Commit(_lib, edit);
                if (r.library != null && !ReferenceEquals(r.library, _lib))
                {
                    _lib = r.library;
                    Revision++;
                }
                if (r.mergedWithDisk)
                    WBD_Log.Msg("Library.xml had been changed by another game; this change was made on that newer version (nothing of either game is lost).");
                if (r.newerCopy != null)
                    WBD_Log.Msg("Library.xml came from a newer version of Set my Defaults; a copy was kept as " + System.IO.Path.GetFileName(r.newerCopy) + ".");
                if (r.saved)
                {
                    LastSaveError = null;
                    return true;
                }
                if (r.error != null)
                {
                    SetSaveError(r.error, r.kind, r.detail);
                    if (r.diskUnreadable)
                        SetLoadError(r.error, r.kind, r.detail);
                    WBD_Log.Warn("Could not save the library: " + r.error + ". Nothing was changed.");
                }
                return false;
            }
            catch (Exception e)
            {
                SetSaveError(e.GetType().Name + ": " + e.Message, LibErrorKind.WriteFailed, e.GetType().Name + ": " + e.Message);
                WBD_Log.Warn("Could not save the library: " + e);
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
            bool ok = Mutate(lib =>
            {
                result = LegacyMigration.ImportAll(SmdPaths.LegacyRoot, lib, overwrite);
                return result.AnythingFound;
            });
            return ok || (result != null && !result.AnythingFound) ? result : null;
        }

        /// <summary>
        /// Puts Library.xml.bak back (the unreadable or unwanted current file is kept as Library.broken-&lt;time&gt;.xml) and reads it.
        /// Returns null on success, else the reason.
        /// </summary>
        public static string RestoreBackup()
        {
            string err = LibraryFile.RestoreBackup(SmdPaths.LibraryFile, out string keptAs);
            if (err != null)
            {
                WBD_Log.Warn("Could not restore Library.xml.bak: " + err);
                return err;
            }
            WBD_Log.Msg("Library.xml.bak was restored as Library.xml" + (keptAs != null ? "; the previous file was kept as " + System.IO.Path.GetFileName(keptAs) : "") + ".");
            Reload();
            return ReadOnly ? LoadError : null;
        }

        /// <summary>Drop the cached library so the next access reads the file again (tests, "reload" button).</summary>
        public static void Reload()
        {
            _loaded = false;
            _lib = null;
            _disk = null;
            SetLoadError(null, LibErrorKind.None, null);
            SetSaveError(null, LibErrorKind.None, null);
            UsingBackup = false;
            EnsureLoaded();
        }
    }

    /// <summary>Words the file layer's error kinds in the player's language; the technical detail (exception text) stays as it is.</summary>
    public static class SmdFileText
    {
        public static string Explain(LibErrorKind kind, string detail, string english)
        {
            string key;
            switch (kind)
            {
                case LibErrorKind.Unreadable: key = "WBD_FileErrUnreadable"; break;
                case LibErrorKind.ChangedUnreadable: key = "WBD_FileErrChangedUnreadable"; break;
                case LibErrorKind.FolderNotCreatable: key = "WBD_FileErrFolderNotCreatable"; break;
                case LibErrorKind.FolderNotWritable: key = "WBD_FileErrFolderNotWritable"; break;
                case LibErrorKind.Locked: key = "WBD_FileErrLocked"; break;
                case LibErrorKind.WriteFailed: key = "WBD_FileErrWriteFailed"; break;
                default: return english;
            }
            if (!key.CanTranslate()) return english;
            string text = key.Translate().ToString();
            return string.IsNullOrEmpty(detail) ? text : text + " (" + detail + ")";
        }
    }
}
