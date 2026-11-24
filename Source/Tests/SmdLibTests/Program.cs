using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using BetterWorkbenchDefaults.Lib;

// Proof suite for Source/Lib. Every check prints PASS/FAIL; the exit code is the number of failures.
// The fixtures in ../fixtures/legacy_v13 are written in the exact layout the 1.x Scribe code produced
// (element names from the 1.x ExposeData methods, Scribe value formats "0~20", "Awful~Legendary", IsNull="True",
// default values omitted), including defs of mods that are not installed.
internal static class Program
{
    private static int _fail;
    private static int _pass;

    private static void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    }

    private static string FixtureRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "fixtures", "legacy_v13");
            if (Directory.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir, "..", "fixtures", "legacy_v13");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        throw new DirectoryNotFoundException("fixtures/legacy_v13 not found");
    }

    private static Dictionary<string, string> Hashes(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f.Substring(root.Length), LegacyMigration.Sha256);

    private static XElement SourceConfig(string file, string presetName)
    {
        var root = XDocument.Load(file).Root;
        return root.Element("presets").Elements("li").First(li => (string)li.Element("name") == presetName).Element("config");
    }

    private static int Main()
    {
        var fixtures = FixtureRoot();
        var work = Path.Combine(Path.GetTempPath(), "smdlibtests_" + Guid.NewGuid().ToString("N"));
        // work on a copy so the fixture folder can never be changed by a bug
        foreach (var f in Directory.GetFiles(fixtures, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(work, f.Substring(fixtures.Length).TrimStart('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(f, dst);
        }
        try
        {
            Migration(work);
            RoundTrip(work);
            Share();
            Rules();
            CorruptFile(work);
        }
        catch (Exception e)
        {
            Check(false, "unexpected exception: " + e);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
        Console.WriteLine();
        Console.WriteLine(_pass + " passed, " + _fail + " failed");
        return _fail;
    }

    // ---------------- migration ----------------

    private static void Migration(string root)
    {
        var before = Hashes(root);
        Check(LegacyMigration.HasLegacyFiles(root), "legacy files detected");
        var lib = new SmdLibrary();
        var r = LegacyMigration.ImportAll(root, lib, overwrite: false);
        Console.WriteLine("     migration: " + r);

        Check(r.errors.Count == 1 && r.errors[0].StartsWith("Broken.xml"), "the broken file is reported, the others still import");
        Check(r.buildingSets == 4, "4 building sets imported (ElectricStove, Shelf, TableButcher, PRF_SAL_Smart): " + r.buildingSets);
        Check(r.buildingPresets == 5, "5 building presets: " + r.buildingPresets);
        Check(r.pawnPresets == 2, "2 pawn presets: " + r.pawnPresets);
        Check(r.policies, "policy set imported");

        var stove = lib.content.Buildings("ElectricStove");
        Check(stove != null && stove.presets.Count == 2, "stove has 2 presets");
        Check(stove?.defaultName == "Kitchen", "stove default kept (Kitchen)");
        var fancy = "Lavish & \"fancy\" <night>";
        Check(stove?.Get(fancy) != null, "preset name with XML special characters kept exactly");

        var src = Path.Combine(root, "WorkbenchBillDefaults", "ElectricStove.xml");
        Check(XNode.DeepEquals(stove.Get("Kitchen").config, SourceConfig(src, "Kitchen")),
            "Kitchen config is element-for-element identical to the 1.x file");
        Check(XNode.DeepEquals(stove.Get(fancy).config, SourceConfig(src, fancy)), "second preset identical");
        var kitchenXml = stove.Get("Kitchen").config.ToString();
        Check(kitchenXml.Contains("VCE_CookStewSimple") && kitchenXml.Contains("VCE_RawSnail") && kitchenXml.Contains("VCE_Snail"),
            "bills and filter entries of a missing mod (VCE) are kept, not pruned (M4/B4)");

        var butcher = lib.content.Buildings("TableButcher");
        Check(butcher != null && butcher.presets.Count == 1 && butcher.defaultName == LegacyMigration.LegacyDefaultName,
            "v2 single-config file becomes one preset named Default");
        Check(butcher?.Get("Default")?.config.Element("bills")?.Elements("li").Count() == 1, "v2 bills copied");

        var prf = lib.content.Buildings("PRF_SAL_Smart");
        Check(prf != null, "file without <workbench> falls back to the file name as key");
        Check(prf?.Get("Default")?.config.Element("storage") == null, "IsNull storage is not copied");

        var shelf = lib.content.Buildings("Shelf");
        Check(shelf?.Get("Default")?.config.Element("storage")?.Element("priority")?.Value == "Important", "shelf storage priority kept");

        var pawns = lib.content.pawns;
        Check(pawns != null && pawns.presets.Count == 2 && pawns.defaultName == "Worker", "pawn presets + default (Worker) kept");
        var pawnsSrc = Path.Combine(root, "PawnDefaults", "Pawns.xml");
        var srcPawns = XDocument.Load(pawnsSrc).Root.Element("presets").Elements("li").ToList();
        Check(srcPawns.All(li => XNode.DeepEquals(pawns.Get((string)li.Element("name")).config, li.Element("config"))),
            "pawn configs identical to the 1.x file (timetable, work dictionary, policies, misc)");

        var pol = lib.content.policies;
        Check(pol != null && pol.config.Element("version") == null && pol.config.Element("apparelDefault")?.Value == "Anything"
              && pol.config.Element("foodDefault")?.Value == "Lavish" && pol.config.Element("apparel") != null,
            "policy set copied without its version node");

        var after = Hashes(root);
        Check(before.Count == after.Count && before.All(kv => after[kv.Key] == kv.Value), "no 1.x file was changed, moved or deleted");
        Check(lib.migrationLog.Count == 6, "migration log lists the 6 imported files: " + lib.migrationLog.Count);

        // idempotence: importing again without overwrite keeps everything and adds nothing
        var snapshot = lib.ToXml().ToString();
        var r2 = LegacyMigration.ImportAll(root, lib, overwrite: false);
        lib.migrationLog.RemoveRange(6, lib.migrationLog.Count - 6);
        Check(r2.buildingPresets == 0 && r2.pawnPresets == 0 && !r2.policies && r2.skippedExisting == 8,
            "re-import without overwrite adds nothing and keeps 8 existing entries: " + r2);
        Check(lib.ToXml().ToString() == snapshot, "library unchanged by the second import");

        // a preset edited in 2.0 is not replaced by a re-import, but is with overwrite
        stove.Get("Kitchen").config = new XElement("config", new XElement("bills"));
        LegacyMigration.ImportAll(root, lib, overwrite: false);
        Check(!stove.Get("Kitchen").config.Element("bills").HasElements, "re-import keeps the player's newer preset");
        LegacyMigration.ImportAll(root, lib, overwrite: true);
        Check(XNode.DeepEquals(lib.content.Buildings("ElectricStove").Get("Kitchen").config, SourceConfig(src, "Kitchen")),
            "re-import with overwrite restores the 1.x preset");
    }

    // ---------------- library file ----------------

    private static void RoundTrip(string root)
    {
        var lib = new SmdLibrary();
        LegacyMigration.ImportAll(root, lib, false);
        lib.unknownElements.Add(new XElement("futureSection", new XAttribute("x", "1"), new XElement("keep", "me")));
        var profile = new SmdProfile { name = "Tribal start", description = "Neolithic, no stoves", createdAt = "2026-11-24", content = lib.content.Subset(ProfileParts.Pawns | ProfileParts.Policies) };
        lib.profiles.Add(profile);

        var path = Path.Combine(root, "out", LibraryFile.FileName);
        LibraryFile.Save(path, lib);
        var text1 = File.ReadAllText(path);
        var loaded = LibraryFile.Load(path);
        Check(loaded.error == null && loaded.library != null, "Library.xml loads back");
        Check(XNode.DeepEquals(loaded.library.ToXml(), lib.ToXml()), "Library.xml round trip is lossless");
        LibraryFile.Save(path, loaded.library);
        Check(File.ReadAllText(path) == text1, "writing the loaded library again gives the identical file (stable order)");
        Check(File.Exists(path + ".bak"), "previous version kept as Library.xml.bak");
        Check(!File.Exists(path + ".tmp"), "no temp file left behind");
        Check(loaded.library.unknownElements.Count == 1 && loaded.library.unknownElements[0].Element("keep")?.Value == "me",
            "unknown top-level section of a newer version is kept");
        var lp = loaded.library.Profile("Tribal start");
        Check(lp != null && lp.description == "Neolithic, no stoves" && lp.content.PartsPresent() == (ProfileParts.Pawns | ProfileParts.Policies),
            "profile with description and parts survives");
        Check(loaded.library.version == SmdLibrary.CurrentVersion, "version attribute is " + SmdLibrary.CurrentVersion);
    }

    // ---------------- share strings ----------------

    private static void Share()
    {
        var content = new SmdContent();
        var set = content.GetOrAddBuildings("ElectricStove");
        set.AddOrReplace(new StoredPreset
        {
            name = "Kitchen",
            requiredMods = { new ModRef("vanillaexpanded.vcooke", "Vanilla Cooking Expanded") },
            config = new XElement("config", new XElement("bills", new XElement("li", new XElement("recipe", "VCE_CookStewSimple"))))
        }, true);
        var p = new SmdProfile { name = "Share me", content = content };
        var s = ShareCodec.Encode(p);
        Check(s.StartsWith(ShareCodec.Prefix), "share string has the SMD1: prefix");
        var back = ShareCodec.Decode(s);
        Check(XNode.DeepEquals(back.ToXml(), p.ToXml()), "share string round trip is lossless");
        Check(back.content.Buildings("ElectricStove").Get("Kitchen").requiredMods[0].name == "Vanilla Cooking Expanded",
            "required mod travels with the share string");

        // pasted through a chat program: line breaks every 60 characters, spaces, lower-case prefix
        var mangled = new StringBuilder("smd1:");
        var body = s.Substring(ShareCodec.Prefix.Length);
        for (int i = 0; i < body.Length; i++)
        {
            if (i > 0 && i % 60 == 0) mangled.Append("\r\n  ");
            mangled.Append(body[i]);
        }
        Check(XNode.DeepEquals(ShareCodec.Decode(mangled.ToString()).ToXml(), p.ToXml()), "line breaks, spaces and prefix case are tolerated");

        Check(Throws(() => ShareCodec.Decode(s.Substring(0, s.Length - 12))), "a cut-off string is rejected with a FormatException");
        Check(Throws(() => ShareCodec.Decode("hello")), "a string without prefix is rejected");
        Check(Throws(() => ShareCodec.Decode(ShareCodec.Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes("plain")))), "non-gzip payload rejected");

        // gzip bomb: 9 MB of zeros must not be inflated beyond the limit
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
            {
                var zeros = new byte[1024 * 1024];
                for (int i = 0; i < 9; i++) gz.Write(zeros, 0, zeros.Length);
            }
            Check(Throws(() => ShareCodec.Decode(ShareCodec.Prefix + Convert.ToBase64String(ms.ToArray()))), "oversized payload (gzip bomb) rejected");
        }

        // DTD / entity expansion is refused
        var evil = "<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY x \"xxxxxxxx\">]><SetMyDefaultsShare version=\"1\"><profile name=\"&x;\"/></SetMyDefaultsShare>";
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
            {
                var b = Encoding.UTF8.GetBytes(evil);
                gz.Write(b, 0, b.Length);
            }
            Check(Throws(() => ShareCodec.Decode(ShareCodec.Prefix + Convert.ToBase64String(ms.ToArray()))), "DTD in a share string is refused");
        }
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (FormatException) { return true; }
        catch (Exception e) { Console.WriteLine("     (threw " + e.GetType().Name + ": " + e.Message + ")"); return false; }
    }

    // ---------------- preset/profile rules ----------------

    private static void Rules()
    {
        var set = new PresetSet { key = "X" };
        set.AddOrReplace(new StoredPreset { name = "A", config = new XElement("config") }, false);
        set.AddOrReplace(new StoredPreset { name = "B", config = new XElement("config") }, false);
        Check(set.defaultName == "A", "first preset becomes the default");
        Check(!set.Rename("A", "B") && !set.Rename("A", "  "), "rename to a taken or empty name is refused");
        Check(set.Rename("A", "C") && set.defaultName == "C", "rename carries the default");
        Check(set.Remove("C") && set.defaultName == "B", "removing the default moves it to the first remaining");

        var mine = new SmdContent();
        mine.GetOrAddBuildings("Stove").AddOrReplace(new StoredPreset { name = "Mine", config = new XElement("config", "1") }, true);
        mine.GetOrAddBuildings("Stove").AddOrReplace(new StoredPreset { name = "Shared", config = new XElement("config", "old") }, false);
        var inc = new SmdContent();
        inc.GetOrAddBuildings("Stove").AddOrReplace(new StoredPreset { name = "Shared", config = new XElement("config", "new") }, true);
        inc.GetOrAddBuildings(SmdContent.ZonePrefix + "growing").AddOrReplace(new StoredPreset { name = "Rice", config = new XElement("config") }, true);
        inc.colony = new StoredBlock { config = new XElement("config", new XElement("autoRebuild", "True")) };
        int n = mine.MergeFrom(inc, ProfileParts.Buildings);
        Check(n == 1 && mine.Buildings("Stove").Get("Shared").config.Value == "new", "merge replaces the same-named preset");
        Check(mine.Buildings("Stove").defaultName == "Mine", "merge keeps the existing default");
        Check(mine.Buildings("zone:growing") == null && mine.colony == null, "merge honours the chosen parts (no zones, no colony)");
        mine.MergeFrom(inc, ProfileParts.All);
        Check(mine.Buildings("zone:growing")?.defaultName == "Rice" && mine.colony != null, "merge of all parts adds zones and colony");

        var lib = new SmdLibrary();
        lib.profiles.Add(new SmdProfile { name = "P" });
        Check(lib.UniqueProfileName("P") == "P (2)" && lib.UniqueProfileName("Q") == "Q", "unique profile names");
    }

    // ---------------- damaged library ----------------

    private static void CorruptFile(string root)
    {
        var path = Path.Combine(root, "bad", LibraryFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "<SetMyDefaultsLibrary version=\"4\"><buildings><set key=\"A\"");
        var hash = LegacyMigration.Sha256(path);
        var r = LibraryFile.Load(path);
        Check(r.existed && r.library == null && r.error != null, "a damaged Library.xml is reported, not replaced: " + r.error);
        Check(LegacyMigration.Sha256(path) == hash, "the damaged file is left byte-for-byte untouched");
        File.WriteAllText(path, "<SomethingElse/>");
        Check(LibraryFile.Load(path).error != null, "a file with the wrong root is refused");
    }
}
