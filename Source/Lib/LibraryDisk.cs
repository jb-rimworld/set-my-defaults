using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BetterWorkbenchDefaults.Lib
{
    /// <summary>
    /// One game's view of Library.xml on disk. Several games can run at once (two RimWorld windows, a test instance) and all of
    /// them write the same file, so a change is never written blindly over the file:
    /// <list type="bullet">
    /// <item>A short lock file (Library.xml.lock, created exclusively) makes read-change-write one step across processes; a lock left
    /// behind by a crashed game counts as stale after <see cref="StaleLockMs"/>.</item>
    /// <item>Under the lock the file is read again. When it is not the version this game last read or wrote (its SHA-256 differs),
    /// another game changed it: the change is made on THAT version instead of this game's copy, so both games' presets survive.</item>
    /// <item>A file that changed and cannot be read (a hand edit with a typo) is not overwritten.</item>
    /// <item>A file written by a newer version of the mod is copied once to Library.format&lt;N&gt;.xml before this version writes it
    /// (unknown parts are kept anyway, see <see cref="XExtra"/>; the copy is the safety net).</item>
    /// </list>
    /// Verse-free (proven by Source/Tests/SmdLibTests).
    /// </summary>
    public sealed class LibraryDisk
    {
        public readonly string path;
        public int LockWaitMs = 4000;
        public int StaleLockMs = 20000;

        /// <summary>SHA-256 of the file as this game last read or wrote it; null = no file (or never read).</summary>
        public string KnownHash { get; private set; }
        long knownLength = -1, knownTicks = -1;

        public LibraryDisk(string path) { this.path = path; }

        public string LockPath => path + ".lock";

        public static string HashOf(byte[] bytes)
        {
            if (bytes == null)
                return null;
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(bytes);
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        void Remember(byte[] bytes)
        {
            KnownHash = HashOf(bytes);
            try
            {
                var fi = new FileInfo(path);
                if (bytes != null && fi.Exists) { knownLength = fi.Length; knownTicks = fi.LastWriteTimeUtc.Ticks; }
                else { knownLength = -1; knownTicks = -1; }
            }
            catch { knownLength = -1; knownTicks = -1; }
        }

        /// <summary>Reads the file (and remembers its hash as the version this game knows).</summary>
        public LibraryFile.LoadResult Load()
        {
            var r = LibraryFile.Load(path);
            if (r.library != null || !r.existed)
                Remember(r.bytes);
            return r;
        }

        /// <summary>
        /// True when the file on disk is not the version this game last read or wrote. Cheap when nothing changed (size and time
        /// first); the content is hashed only when those differ, so touching the file without changing it is not a change.
        /// </summary>
        public bool ChangedOnDisk()
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists)
                    return KnownHash != null;
                if (fi.Length == knownLength && fi.LastWriteTimeUtc.Ticks == knownTicks)
                    return false;
                var h = HashOf(File.ReadAllBytes(path));
                if (h == KnownHash)
                {
                    knownLength = fi.Length; knownTicks = fi.LastWriteTimeUtc.Ticks;
                    return false;
                }
                return true;
            }
            catch
            {
                return false;   // cannot look right now (being replaced): next time
            }
        }

        public sealed class CommitResult
        {
            /// <summary>The change was written.</summary>
            public bool saved;
            /// <summary>The library this game should use from now on (the written one, or the newer one found on disk); null = keep.</summary>
            public SmdLibrary library;
            /// <summary>Another game had changed the file; the change was made on its version.</summary>
            public bool mergedWithDisk;
            /// <summary>The file changed on disk and cannot be read: nothing was written.</summary>
            public bool diskUnreadable;
            /// <summary>A file of a newer format was copied here before this version wrote it (null = no copy made).</summary>
            public string newerCopy;
            /// <summary>Why nothing was saved (null when <see cref="saved"/>, or when the edit itself had nothing to do).</summary>
            public string error;
        }

        /// <summary>
        /// Change the library: <paramref name="edit"/> works on a CLONE of the newest version (this game's <paramref name="current"/>,
        /// or the file on disk when another game changed it) and returns false to abort. Written atomically under the lock.
        /// </summary>
        public CommitResult Commit(SmdLibrary current, Func<SmdLibrary, bool> edit)
        {
            var res = new CommitResult();
            FileStream lck;
            string lockErr = TryLock(out lck);
            if (lockErr != null)
            {
                res.error = lockErr;
                return res;
            }
            try
            {
                var basis = current ?? new SmdLibrary();
                byte[] onDisk = File.Exists(path) ? File.ReadAllBytes(path) : null;
                string h = HashOf(onDisk);
                if (h != KnownHash && onDisk != null)
                {
                    try
                    {
                        basis = LibraryFile.Parse(onDisk);
                    }
                    catch (Exception e)
                    {
                        res.diskUnreadable = true;
                        res.error = "Library.xml was changed outside this game and cannot be read (" + e.GetType().Name + ": " + e.Message + ")";
                        return res;
                    }
                    res.mergedWithDisk = true;
                    res.library = basis;
                    Remember(onDisk);
                }
                // (a file deleted meanwhile is simply written again from this game's copy)
                var working = basis.Clone();
                if (!edit(working))
                    return res;
                working.content.RemoveEmpty();
                if (onDisk != null && basis.FileVersion > SmdLibrary.CurrentVersion)
                    res.newerCopy = KeepNewerCopy(onDisk, basis.FileVersion);
                var written = LibraryFile.Save(path, working);
                Remember(written);
                res.saved = true;
                res.library = working;
                return res;
            }
            catch (Exception e)
            {
                res.error = e.GetType().Name + ": " + e.Message;
                return res;
            }
            finally
            {
                Unlock(lck);
            }
        }

        /// <summary>Library.format&lt;N&gt;.xml, once per format (an existing copy is never replaced).</summary>
        string KeepNewerCopy(byte[] bytes, int version)
        {
            var copy = Path.Combine(Path.GetDirectoryName(path) ?? "", "Library.format" + version + ".xml");
            if (File.Exists(copy))
                return null;
            File.WriteAllBytes(copy, bytes);
            return copy;
        }

        /// <summary>Takes the lock (waits up to <see cref="LockWaitMs"/>). Returns null on success, else the reason.</summary>
        string TryLock(out FileStream lck)
        {
            lck = null;
            var sw = Stopwatch.StartNew();
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                return "the library folder cannot be created (" + e.Message + ")";
            }
            while (true)
            {
                try
                {
                    lck = new FileStream(LockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var b = Encoding.UTF8.GetBytes(Process.GetCurrentProcess().Id + " " + DateTime.UtcNow.ToString("o"));
                    lck.Write(b, 0, b.Length);
                    lck.Flush();
                    return null;
                }
                catch (IOException)
                {
                    try
                    {
                        var fi = new FileInfo(LockPath);
                        if (fi.Exists && (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalMilliseconds > StaleLockMs)
                        {
                            File.Delete(LockPath);   // left behind by a game that crashed while writing
                            continue;
                        }
                    }
                    catch { }
                    if (sw.ElapsedMilliseconds > LockWaitMs)
                        return "another game is writing the library right now (Library.xml.lock); try again in a moment";
                    Thread.Sleep(20);
                }
                catch (UnauthorizedAccessException e)
                {
                    return "the library folder is not writable (" + e.Message + ")";
                }
            }
        }

        void Unlock(FileStream lck)
        {
            if (lck == null)
                return;
            try { lck.Dispose(); } catch { }
            try { File.Delete(LockPath); } catch { }
        }
    }
}
