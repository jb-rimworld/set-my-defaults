using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BetterWorkbenchDefaults.Lib
{
    /// <summary>XML parsing without DTDs or external entities (library files and share strings come from players).</summary>
    public static class XmlSafe
    {
        private static XmlReaderSettings Settings => new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            MaxCharactersInDocument = 64L * 1024 * 1024
        };

        public static XDocument LoadFile(string path)
        {
            using (var r = XmlReader.Create(path, Settings))
                return XDocument.Load(r);
        }

        public static XDocument Parse(string text)
        {
            using (var sr = new StringReader(text))
            using (var r = XmlReader.Create(sr, Settings))
                return XDocument.Load(r);
        }

        public static XDocument Parse(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes, false))
            using (var r = XmlReader.Create(ms, Settings))
                return XDocument.Load(r);
        }
    }

    /// <summary>
    /// Reads and writes Library.xml. Writing is atomic (a temp file with a name of its own, then replace) and keeps the previous version as
    /// Library.xml.bak. A file that cannot be parsed is never overwritten: <see cref="Load"/> reports the error and the store goes
    /// read-only for the session, so a typo in a hand-edited file cannot wipe a player's presets. Several games at once and the
    /// concurrency rules live in <see cref="LibraryDisk"/>.
    /// </summary>
    public static class LibraryFile
    {
        public const string FileName = "Library.xml";
        public const string BackupSuffix = ".bak";

        public sealed class LoadResult
        {
            public SmdLibrary library;   // null when the file did not exist or could not be read
            public bool existed;
            public string error;         // set when the file exists but could not be read (English, for logs)
            public string detail;        // the technical part of error (exception type and message)
            public byte[] bytes;         // what was read (null when the file did not exist)
        }

        public static LoadResult Load(string path)
        {
            var r = new LoadResult();
            if (!File.Exists(path))
                return r;
            r.existed = true;
            try
            {
                r.bytes = File.ReadAllBytes(path);
                r.library = Parse(r.bytes);
            }
            catch (Exception e)
            {
                r.library = null;
                r.detail = e.GetType().Name + ": " + e.Message;
                r.error = r.detail;
            }
            return r;
        }

        public static SmdLibrary Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new FormatException("the file is empty");
            return SmdLibrary.FromXml(XmlSafe.Parse(bytes).Root);
        }

        public static byte[] Serialize(SmdLibrary lib)
        {
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), lib.ToXml());
            var ws = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
            using (var ms = new MemoryStream())
            {
                using (var w = XmlWriter.Create(ms, ws))
                    doc.Save(w);
                return ms.ToArray();
            }
        }

        /// <summary>Writes the library and returns the bytes written. Throws when it could not (the old file is then unchanged).</summary>
        public static byte[] Save(string path, SmdLibrary lib)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var bytes = Serialize(lib);
            // sanity: what we write must parse back before it replaces anything
            Parse(bytes);
            // A temp name of its own: a leftover temp file of a crash (even a read-only one) or another game writing at the same
            // moment can never block or mix with this write.
            var tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, bytes);   // disk full throws here; Library.xml is untouched
                if (File.Exists(path))
                {
                    // the backup is a convenience: a backup that cannot be written (read-only .bak) never blocks the save
                    try { CopyOver(path, path + BackupSuffix); }
                    catch { }
                }
                MoveOver(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
            return bytes;
        }

        /// <summary>Moves a finished temp file over the target in one rename, so a reader never sees a half-written file.</summary>
        static void MoveOver(string tmp, string path)
        {
#if RW16
            // net472 has no File.Move overwrite; File.Replace is the same single rename on Mono (needs an existing target).
            // Never run on a 1.6 game: if this Mono's File.Replace refuses (read-only target, a file system without the call),
            // copy over the target instead (CopyOver clears a read-only flag, as File.Move does on 1.7) and drop the temp file.
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException || e is NotSupportedException)
            {
                CopyOver(tmp, path);
                try { File.Delete(tmp); } catch { }
            }
#else
            File.Move(tmp, path, true);
#endif
        }

        static void CopyOver(string from, string to)
        {
            try { File.Copy(from, to, true); }
            catch (UnauthorizedAccessException)
            {
                File.SetAttributes(to, FileAttributes.Normal);
                File.Copy(from, to, true);
            }
        }

        /// <summary>Removes temp files of earlier writes that crashed (older than a minute, so another game's write in progress is left alone).</summary>
        public static int CleanStaleTemps(string path)
        {
            int n = 0;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return 0;
                foreach (var f in Directory.GetFiles(dir, Path.GetFileName(path) + "*.tmp"))
                {
                    try
                    {
                        if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalSeconds < 60)
                            continue;
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// Puts Library.xml.bak back as Library.xml. The current file (unreadable or not) is never deleted: it is renamed to
        /// Library.broken-&lt;time&gt;.xml first. Returns null on success, else the reason; <paramref name="keptAs"/> names the kept file.
        /// </summary>
        public static string RestoreBackup(string path, out string keptAs)
        {
            keptAs = null;
            var bak = path + BackupSuffix;
            var b = Load(bak);
            if (!b.existed)
                return "there is no " + Path.GetFileName(bak);
            if (b.library == null)
                return Path.GetFileName(bak) + " cannot be read either (" + b.error + ")";
            try
            {
                if (File.Exists(path))
                {
                    var dir = Path.GetDirectoryName(path) ?? "";
                    keptAs = Path.Combine(dir, "Library.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml");
                    for (int i = 2; File.Exists(keptAs); i++)
                        keptAs = Path.Combine(dir, "Library.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + i + ".xml");
                    File.Move(path, keptAs);
                }
                var tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp";
                File.WriteAllBytes(tmp, b.bytes);
                MoveOver(tmp, path);
                return null;
            }
            catch (Exception e)
            {
                return e.GetType().Name + ": " + e.Message;
            }
        }
    }

    /// <summary>
    /// Share strings: "SMD1:" + base64(gzip(UTF-8 XML of one &lt;SetMyDefaultsShare&gt; element)). Line breaks and spaces
    /// inside the string are ignored, so a string survives being pasted through chat programs.
    /// </summary>
    public static class ShareCodec
    {
        public const string Prefix = "SMD1:";
        public const string RootName = "SetMyDefaultsShare";
        public const int MaxDecodedBytes = 8 * 1024 * 1024;

        public static string Encode(SmdProfile profile)
        {
            var root = new XElement(RootName, new XAttribute("version", 1), profile.ToXml());
            var bytes = Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                    gz.Write(bytes, 0, bytes.Length);
                return Prefix + Convert.ToBase64String(ms.ToArray());
            }
        }

        /// <summary>Decode a share string into a profile. Throws <see cref="FormatException"/> with a readable reason.</summary>
        public static SmdProfile Decode(string share)
        {
            if (string.IsNullOrWhiteSpace(share))
                throw new FormatException("empty");
            var sb = new StringBuilder(share.Length);
            foreach (var c in share)
                if (!char.IsWhiteSpace(c))
                    sb.Append(c);
            var s = sb.ToString();
            if (!s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("missing " + Prefix + " prefix");
            byte[] packed;
            try
            {
                packed = Convert.FromBase64String(s.Substring(Prefix.Length));
            }
            catch (FormatException)
            {
                throw new FormatException("not valid base64 (cut off while copying?)");
            }
            byte[] xml;
            try
            {
                using (var input = new MemoryStream(packed))
                using (var gz = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buf = new byte[16384];
                    int n;
                    while ((n = gz.Read(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, n);
                        if (output.Length > MaxDecodedBytes)
                            throw new FormatException("too large");
                    }
                    xml = output.ToArray();
                }
            }
            catch (InvalidDataException)
            {
                throw new FormatException("damaged (not a gzip payload)");
            }
            // .NET does not insist on the gzip trailer, so a string that lost its end can still inflate. The trailer's
            // last 4 bytes hold the original length (ISIZE); a cut-off string fails this check.
            if (packed.Length < 18 || BitConverter.ToUInt32(packed, packed.Length - 4) != (uint)xml.Length)
                throw new FormatException("incomplete (cut off while copying?)");
            XElement root;
            try
            {
                root = XmlSafe.Parse(Encoding.UTF8.GetString(xml)).Root;
            }
            catch (XmlException e)
            {
                throw new FormatException("damaged XML: " + e.Message);
            }
            if (root == null || root.Name.LocalName != RootName)
                throw new FormatException("not a Set my Defaults share string");
            var p = root.Element("profile");
            if (p == null)
                throw new FormatException("share string has no profile");
            var profile = SmdProfile.FromXml(p);
            profile.content.RemoveEmpty();
            return profile;
        }
    }
}
