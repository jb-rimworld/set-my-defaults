using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace BetterWorkbenchDefaults.Lib
{
    /// <summary>
    /// One-way import of the 1.x preset files into the 2.0 library. The old files are only READ, never moved,
    /// changed or deleted (the frozen 1.6 build keeps using them). The stored &lt;config&gt; elements are copied
    /// verbatim, so every value, every unknown recipe and every def of a switched-off mod survives exactly.
    ///
    /// Sources (all next to RimWorld's Config folder):
    ///  - WorkbenchBillDefaults/&lt;defName&gt;.xml, root &lt;workbenchBillDefaults&gt;:
    ///      v3: version, workbench, defaultPreset, presets/li(name, config)
    ///      v1/v2: workbench?, bills, storage, splitter directly under the root (one unnamed default)
    ///  - PawnDefaults/Pawns.xml, root &lt;pawnDefaults&gt;: version, defaultPreset, presets/li(name, config)
    ///  - PawnDefaults/PolicyDefaults.xml, root &lt;policyDefaults&gt;: version, apparel, drug, food, reading,
    ///      apparelDefault, drugDefault, foodDefault, readingDefault
    /// </summary>
    public static class LegacyMigration
    {
        public const string WorkbenchFolder = "WorkbenchBillDefaults";
        public const string PawnFolder = "PawnDefaults";
        public const string PawnsFile = "Pawns.xml";
        public const string PoliciesFile = "PolicyDefaults.xml";
        public const string LegacyDefaultName = "Default";

        public sealed class Result
        {
            public int buildingSets;
            public int buildingPresets;
            public int pawnPresets;
            public bool policies;
            public int skippedExisting;
            public List<string> errors = new List<string>();
            public bool AnythingFound => buildingPresets > 0 || pawnPresets > 0 || policies;
            public override string ToString() =>
                "buildings " + buildingSets + " sets/" + buildingPresets + " presets, pawns " + pawnPresets +
                ", policies " + (policies ? "yes" : "no") + ", kept existing " + skippedExisting +
                (errors.Count > 0 ? ", errors: " + string.Join("; ", errors) : "");
        }

        /// <summary>True when any 1.x preset file exists under <paramref name="configRoot"/>.</summary>
        public static bool HasLegacyFiles(string configRoot)
        {
            try
            {
                var wb = Path.Combine(configRoot, WorkbenchFolder);
                if (Directory.Exists(wb) && Directory.GetFiles(wb, "*.xml").Length > 0)
                    return true;
                var pd = Path.Combine(configRoot, PawnFolder);
                return File.Exists(Path.Combine(pd, PawnsFile)) || File.Exists(Path.Combine(pd, PoliciesFile));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Import every 1.x file under <paramref name="configRoot"/> into <paramref name="lib"/>. A preset that already
        /// exists in the library (same key + same name) is kept as it is unless <paramref name="overwrite"/>.
        /// A file that cannot be parsed is reported and skipped; nothing is ever deleted.
        /// </summary>
        public static Result ImportAll(string configRoot, SmdLibrary lib, bool overwrite)
        {
            var r = new Result();
            var wb = Path.Combine(configRoot, WorkbenchFolder);
            if (Directory.Exists(wb))
            {
                foreach (var file in Directory.GetFiles(wb, "*.xml").OrderBy(f => f, StringComparer.Ordinal))
                {
                    try
                    {
                        var doc = XmlSafe.LoadFile(file);
                        int before = r.buildingPresets;
                        ImportWorkbenchFile(doc.Root, Path.GetFileNameWithoutExtension(file), lib, overwrite, r);
                        if (r.buildingPresets > before)
                            r.buildingSets++;
                        Log(lib, file, configRoot, r.buildingPresets - before);
                    }
                    catch (Exception e)
                    {
                        r.errors.Add(Path.GetFileName(file) + ": " + e.Message);
                    }
                }
            }
            var pawnsPath = Path.Combine(configRoot, PawnFolder, PawnsFile);
            if (File.Exists(pawnsPath))
            {
                try
                {
                    int before = r.pawnPresets;
                    ImportPawnsFile(XmlSafe.LoadFile(pawnsPath).Root, lib, overwrite, r);
                    Log(lib, pawnsPath, configRoot, r.pawnPresets - before);
                }
                catch (Exception e)
                {
                    r.errors.Add(PawnsFile + ": " + e.Message);
                }
            }
            var polPath = Path.Combine(configRoot, PawnFolder, PoliciesFile);
            if (File.Exists(polPath))
            {
                try
                {
                    if (ImportPoliciesFile(XmlSafe.LoadFile(polPath).Root, lib, overwrite, r))
                        Log(lib, polPath, configRoot, 1);
                }
                catch (Exception e)
                {
                    r.errors.Add(PoliciesFile + ": " + e.Message);
                }
            }
            lib.content.RemoveEmpty();
            return r;
        }

        private static void Log(SmdLibrary lib, string file, string root, int entries)
        {
            var rel = file.StartsWith(root, StringComparison.Ordinal) ? file.Substring(root.Length).TrimStart('/', '\\') : Path.GetFileName(file);
            lib.migrationLog.Add(new XElement("file",
                new XAttribute("path", rel.Replace('\\', '/')),
                new XAttribute("sha256", Sha256(file)),
                new XAttribute("entries", entries),
                new XAttribute("at", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))));
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        // ---- per file (public for the tests) ----

        public static void ImportWorkbenchFile(XElement root, string fileKey, SmdLibrary lib, bool overwrite, Result r)
        {
            if (root == null || root.Name.LocalName != "workbenchBillDefaults")
                throw new FormatException("unexpected root " + root?.Name);
            var key = ((string)root.Element("workbench"))?.Trim();
            if (string.IsNullOrEmpty(key))
                key = fileKey; // the file name is the sanitised defName (identical for every real defName)

            var incoming = new List<StoredPreset>();
            string def = (string)root.Element("defaultPreset");
            var list = root.Element("presets");
            if (list != null && !IsNull(list))
            {
                foreach (var li in list.Elements("li"))
                {
                    var name = (string)li.Element("name");
                    var config = li.Element("config");
                    if (string.IsNullOrEmpty(name) || config == null || IsNull(config) || !config.HasElements)
                        continue;
                    incoming.Add(new StoredPreset { name = name, config = new XElement(config) });
                }
            }
            if (incoming.Count == 0)
            {
                // v1/v2: one config directly under the root.
                var cfg = new XElement("config");
                foreach (var n in new[] { "bills", "storage", "splitter" })
                {
                    var el = root.Element(n);
                    if (el != null && !IsNull(el))
                        cfg.Add(new XElement(el));
                }
                if (cfg.HasElements)
                {
                    incoming.Add(new StoredPreset { name = LegacyDefaultName, config = cfg });
                    def = LegacyDefaultName;
                }
            }
            if (incoming.Count == 0)
                return;

            var set = lib.content.GetOrAddBuildings(key);
            bool wasEmpty = set.IsEmpty;
            foreach (var p in incoming)
            {
                if (set.Get(p.name) != null && !overwrite)
                {
                    r.skippedExisting++;
                    continue;
                }
                set.AddOrReplace(p, makeDefault: false);
                r.buildingPresets++;
            }
            if (wasEmpty && def != null)
                set.SetDefault(def);
            set.Normalize();
        }

        public static void ImportPawnsFile(XElement root, SmdLibrary lib, bool overwrite, Result r)
        {
            if (root == null || root.Name.LocalName != "pawnDefaults")
                throw new FormatException("unexpected root " + root?.Name);
            var list = root.Element("presets");
            if (list == null || IsNull(list))
                return;
            if (lib.content.pawns == null)
                lib.content.pawns = new PresetSet { key = "pawns" };
            var set = lib.content.pawns;
            bool wasEmpty = set.IsEmpty;
            foreach (var li in list.Elements("li"))
            {
                var name = (string)li.Element("name");
                var config = li.Element("config");
                if (string.IsNullOrEmpty(name) || config == null || IsNull(config) || !config.HasElements)
                    continue;
                if (set.Get(name) != null && !overwrite)
                {
                    r.skippedExisting++;
                    continue;
                }
                set.AddOrReplace(new StoredPreset { name = name, config = new XElement(config) }, makeDefault: false);
                r.pawnPresets++;
            }
            var def = (string)root.Element("defaultPreset");
            if (wasEmpty && def != null)
                set.SetDefault(def);
            set.Normalize();
        }

        public static bool ImportPoliciesFile(XElement root, SmdLibrary lib, bool overwrite, Result r)
        {
            if (root == null || root.Name.LocalName != "policyDefaults")
                throw new FormatException("unexpected root " + root?.Name);
            if (lib.content.policies != null && !overwrite)
            {
                r.skippedExisting++;
                return false;
            }
            var cfg = new XElement("config", root.Elements().Where(e => e.Name.LocalName != "version").Select(e => new XElement(e)));
            if (!cfg.HasElements)
                return false;
            lib.content.policies = new StoredBlock { config = cfg };
            r.policies = true;
            return true;
        }

        private static bool IsNull(XElement e) => string.Equals((string)e.Attribute("IsNull"), "True", StringComparison.OrdinalIgnoreCase);
    }
}
