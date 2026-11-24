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
    }

    /// <summary>
    /// Reads and writes Library.xml. Writing is atomic (temp file, then replace) and keeps the previous version as
    /// Library.xml.bak. A file that cannot be parsed is never overwritten: <see cref="Load"/> reports the error and
    /// the store goes read-only for the session, so a typo in a hand-edited file cannot wipe a player's presets.
    /// </summary>
    public static class LibraryFile
    {
        public const string FileName = "Library.xml";

        public sealed class LoadResult
        {
            public SmdLibrary library;   // null when the file did not exist or could not be read
            public bool existed;
            public string error;         // set when the file exists but could not be read
        }

        public static LoadResult Load(string path)
        {
            var r = new LoadResult();
            if (!File.Exists(path))
                return r;
            r.existed = true;
            try
            {
                r.library = SmdLibrary.FromXml(XmlSafe.LoadFile(path).Root);
            }
            catch (Exception e)
            {
                r.error = e.GetType().Name + ": " + e.Message;
            }
            return r;
        }

        public static void Save(string path, SmdLibrary lib)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), lib.ToXml());
            var ws = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
            using (var w = XmlWriter.Create(tmp, ws))
                doc.Save(w);
            // sanity: what we wrote must parse back before it replaces anything
            SmdLibrary.FromXml(XmlSafe.LoadFile(tmp).Root);
            if (File.Exists(path))
                File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
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
