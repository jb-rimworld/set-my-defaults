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
            TwoGames(work);
            NewerVersion(work);
            Duplicates(work);
            TempAndBackup(work);
            FailedWrite(work);
            Replaced();
            BigLibrary(work);
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
    // ---------------- review 24.11.2026: data-loss cases ----------------

    private static StoredPreset P(string name, string body) =>
        new StoredPreset { name = name, config = new XElement("config", new XElement("x", body)) };

    /// <summary>Two games write the same library: neither game's new presets may be lost (finding 1).</summary>
    private static void TwoGames(string root)
    {
        var path = Path.Combine(root, "two", LibraryFile.FileName);
        var a = new LibraryDisk(path); var b = new LibraryDisk(path);
        var seed = a.Commit(new SmdLibrary(), lib => { lib.content.GetOrAddBuildings("Shelf").AddOrReplace(P("Base", "0"), true); return true; });
        Check(seed.saved, "game A creates the library");
        var libA = seed.library;
        var libB = b.Load().library;
        Check(libB != null && libB.content.Buildings("Shelf")?.Get("Base") != null, "game B reads it");
        var ra = a.Commit(libA, lib => { lib.content.GetOrAddBuildings("Shelf").AddOrReplace(P("FromA", "a"), false); return true; });
        libA = ra.library;
        Check(ra.saved && !ra.mergedWithDisk, "game A saves a preset (file unchanged since A wrote it)");
        var rb = b.Commit(libB, lib => { lib.content.GetOrAddBuildings("TableButcher").AddOrReplace(P("FromB", "b"), true); return true; });
        Check(rb.saved && rb.mergedWithDisk, "game B notices that A changed the file and adds its change to A's version");
        var disk = LibraryFile.Load(path).library;
        Check(disk.content.Buildings("Shelf")?.Get("FromA") != null && disk.content.Buildings("TableButcher")?.Get("FromB") != null,
            "both games' presets are in the file (no lost update)");
        Check(rb.library.content.Buildings("Shelf")?.Get("FromA") != null, "game B's library now shows A's preset too");
        Check(a.ChangedOnDisk() && !b.ChangedOnDisk(), "A sees the file changed by B; B does not see its own write as a change");
        // A deletes a preset with its old view: B's preset stays
        var rd = a.Commit(libA, lib => lib.content.Buildings("Shelf").Remove("FromA"));
        disk = LibraryFile.Load(path).library;
        Check(rd.saved && rd.mergedWithDisk && disk.content.Buildings("Shelf")?.Get("FromA") == null && disk.content.Buildings("TableButcher")?.Get("FromB") != null,
            "a delete in A on its old view keeps B's preset");
        // the lock: held by a live game → the other waits and then reports, the file is unchanged
        var hash = LegacyMigration.Sha256(path);
        File.WriteAllText(a.LockPath, "other game");
        var c = new LibraryDisk(path) { LockWaitMs = 200 };
        c.Load();
        var rl = c.Commit(disk, lib => { lib.content.GetOrAddBuildings("X").AddOrReplace(P("Y", "y"), true); return true; });
        Check(!rl.saved && rl.error != null && rl.error.Contains("lock") && LegacyMigration.Sha256(path) == hash, "a held lock blocks the write with a reason; file unchanged: " + rl.error);
        File.SetLastWriteTimeUtc(a.LockPath, DateTime.UtcNow.AddMinutes(-5));
        rl = c.Commit(disk, lib => { lib.content.GetOrAddBuildings("X").AddOrReplace(P("Y", "y"), true); return true; });
        Check(rl.saved && !File.Exists(a.LockPath), "a stale lock of a crashed game is taken over and removed after the write");
        // changed on disk into something unreadable: never overwritten
        File.WriteAllText(path, "<SetMyDefaultsLibrary version=\"4\"><buildings>");
        hash = LegacyMigration.Sha256(path);
        var ru = b.Commit(b.Load().library ?? rb.library, lib => { lib.content.GetOrAddBuildings("Z").AddOrReplace(P("Z", "z"), true); return true; });
        Check(!ru.saved && ru.diskUnreadable && LegacyMigration.Sha256(path) == hash, "a file that became unreadable is not overwritten");
    }

    /// <summary>A library written by a newer version keeps everything this version does not know (finding 2).</summary>
    private static void NewerVersion(string root)
    {
        var path = Path.Combine(root, "newer", LibraryFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, @"<SetMyDefaultsLibrary version='5' channel='beta'>
  <buildings sorted='yes'><set key='ElectricStove' default='A' family='stoves'><preset name='A' locked='true'><config><bills/></config><notes>keep me</notes></preset><future/></set></buildings>
  <pawns default='W' order='2'><preset name='W'><requiredMods><mod id='x.y' name='XY' hash='1'/></requiredMods><config><x>1</x></config></preset></pawns>
  <policies pinned='1'><config><a/></config><meta>m</meta></policies>
  <animals><preset name='Pets'/></animals>
  <profiles sort='date'><profile name='P1' autoPick='tribal'><animals><preset name='Pets'/></animals><zones2/></profile><folder name='f'/></profiles>
 </SetMyDefaultsLibrary>");
        var before = File.ReadAllBytes(path);
        var d = new LibraryDisk(path);
        var lib = d.Load().library;
        Check(lib != null && lib.FileVersion == 5, "a newer format (5) loads");
        var r = d.Commit(lib, l => { l.content.GetOrAddBuildings("Shelf").AddOrReplace(P("New", "n"), true); return true; });
        var after = File.ReadAllText(path);
        foreach (var s in new[] { "channel=\"beta\"", "sorted=\"yes\"", "family=\"stoves\"", "locked=\"true\"", "<notes>keep me</notes>", "<future />", "order=\"2\"",
            "hash=\"1\"", "pinned=\"1\"", "<meta>m</meta>", "<animals>", "autoPick=\"tribal\"", "<zones2 />", "sort=\"date\"", "<folder name=\"f\" />" })
            Check(after.Contains(s), "kept after a save by this version: " + s);
        Check(after.Contains("version=\"5\"") && after.Contains("writtenBy=\"" + SmdLibrary.CurrentVersion + "\""), "the file stays marked as format 5 and says who wrote it");
        var copy = Path.Combine(Path.GetDirectoryName(path), "Library.format5.xml");
        Check(r.saved && r.newerCopy != null && File.Exists(copy) && File.ReadAllBytes(copy).SequenceEqual(before), "before the first write a byte copy Library.format5.xml was kept");
        var r2 = d.Commit(r.library, l => { l.content.GetOrAddBuildings("Shelf").AddOrReplace(P("New2", "n"), false); return true; });
        Check(r2.saved && r2.newerCopy == null && File.ReadAllBytes(copy).SequenceEqual(before), "the copy is made once and never replaced");
        // a newer share string keeps its unknown sections in the imported profile
        var share = new XElement("SetMyDefaultsShare", new XAttribute("version", 2),
            new XElement("profile", new XAttribute("name", "N"), new XElement("animals", new XElement("preset", new XAttribute("name", "Pets"))),
                new XElement("buildings", new XElement("set", new XAttribute("key", "S"), new XElement("preset", new XAttribute("name", "p"), new XElement("config"))))));
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) { var by = Encoding.UTF8.GetBytes(share.ToString()); gz.Write(by, 0, by.Length); }
        var prof = ShareCodec.Decode(ShareCodec.Prefix + Convert.ToBase64String(ms.ToArray()));
        Check(prof.ToXml().ToString().Contains("<animals>"), "a newer share string's unknown section is kept in the profile");
    }

    /// <summary>Hand-edited files with duplicate or empty names: nothing is dropped, consistently (finding 14).</summary>
    private static void Duplicates(string root)
    {
        var lib = SmdLibrary.FromXml(XElement.Parse("<SetMyDefaultsLibrary version='4'><buildings>"
            + "<set key='S' default='X'><preset name='X'><config><a>1</a></config></preset><preset name='X'><config><a>2</a></config></preset><preset name=''><config><a>4</a></config></preset></set>"
            + "<set key='S'><preset name='X'><config><a>3</a></config></preset></set></buildings>"
            + "<profiles><profile name='P'/><profile name='P'><description>second</description></profile><profile name=''/></profiles></SetMyDefaultsLibrary>"));
        var s = lib.content.Buildings("S");
        var values = s.presets.Select(p => p.config.Element("a").Value).OrderBy(x => x).ToList();
        Check(values.SequenceEqual(new[] { "1", "2", "3", "4" }), "every duplicate preset is kept (" + string.Join(",", values) + ")");
        Check(s.Get("X").config.Element("a").Value == "1" && s.defaultName == "X", "the first keeps its name and stays the default");
        Check(s.presets.Select(p => p.name).Distinct().Count() == s.presets.Count, "the others got free names: " + string.Join(" | ", s.presets.Select(p => p.name)));
        Check(lib.profiles.Count == 3 && lib.Profile("P (2)")?.description == "second", "duplicate and empty profile names are kept under free names: " + string.Join(" | ", lib.profiles.Select(p => p.name)));
        var again = SmdLibrary.FromXml(lib.ToXml());
        Check(XNode.DeepEquals(again.ToXml(), lib.ToXml()), "after one round trip the names are stable");
    }

    /// <summary>Leftover temp files (even read-only) never block a save; a read-only .bak does not either; Restore backup (findings 5, 14).</summary>
    private static void TempAndBackup(string root)
    {
        var path = Path.Combine(root, "tmp", LibraryFile.FileName);
        var d = new LibraryDisk(path);
        var lib = d.Commit(new SmdLibrary(), l => { l.content.GetOrAddBuildings("A").AddOrReplace(P("one", "1"), true); return true; }).library;
        File.WriteAllText(path + ".tmp", "junk"); File.SetAttributes(path + ".tmp", FileAttributes.ReadOnly);
        File.WriteAllText(path + ".1234.tmp", "junk"); File.SetAttributes(path + ".1234.tmp", FileAttributes.ReadOnly);
        var r = d.Commit(lib, l => { l.content.GetOrAddBuildings("A").AddOrReplace(P("two", "2"), false); return true; });
        Check(r.saved, "a read-only leftover temp file does not block the save");
        File.SetAttributes(path + ".bak", FileAttributes.ReadOnly);
        r = d.Commit(r.library, l => { l.content.GetOrAddBuildings("A").AddOrReplace(P("three", "3"), false); return true; });
        Check(r.saved && LibraryFile.Load(path).library.content.Buildings("A").Get("three") != null, "a read-only Library.xml.bak does not block the save");
        File.SetAttributes(path + ".bak", FileAttributes.Normal);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp")) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddHours(-1));
        int cleaned = LibraryFile.CleanStaleTemps(path);
        Check(cleaned == 2 && Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0, "stale temp files are cleaned at load (" + cleaned + ")");
        // restore: break the file, restore the backup, the broken file is kept
        var bakLib = LibraryFile.Load(path + ".bak").library;
        File.WriteAllText(path, "<broken");
        var err = LibraryFile.RestoreBackup(path, out string kept);
        var now = LibraryFile.Load(path).library;
        Check(err == null && now != null && XNode.DeepEquals(now.ToXml(), bakLib.ToXml()), "Restore backup puts Library.xml.bak back");
        Check(kept != null && File.ReadAllText(kept) == "<broken", "the broken file is kept as " + Path.GetFileName(kept));
        File.WriteAllText(path + ".bak", "<also broken");
        var hash = LegacyMigration.Sha256(path);
        err = LibraryFile.RestoreBackup(path, out kept);
        Check(err != null && kept == null && LegacyMigration.Sha256(path) == hash, "an unreadable backup is not restored, nothing changes: " + err);
    }

    /// <summary>A write that fails half way (disk full, folder not writable) leaves the old file and no temp file (finding 14).</summary>
    private static void FailedWrite(string root)
    {
        var dir = Path.Combine(root, "fail");
        var path = Path.Combine(dir, LibraryFile.FileName);
        var d = new LibraryDisk(path);
        var lib = d.Commit(new SmdLibrary(), l => { l.content.GetOrAddBuildings("A").AddOrReplace(P("one", "1"), true); return true; }).library;
        var hash = LegacyMigration.Sha256(path);
        File.SetAttributes(dir, FileAttributes.ReadOnly);
        try
        {
            new DirectoryInfo(dir).Attributes |= FileAttributes.ReadOnly;
            var r = d.Commit(lib, l => { l.content.GetOrAddBuildings("A").AddOrReplace(P("two", "2"), false); return true; });
            Check(!r.saved && r.error != null && LegacyMigration.Sha256(path) == hash, "a failed write reports why and leaves Library.xml as it was: " + r.error);
        }
        finally
        {
            File.SetAttributes(dir, FileAttributes.Directory);
        }
        Check(Directory.GetFiles(dir, "*.tmp").Length == 0 && !File.Exists(path + ".lock"), "no temp or lock file left behind");
    }

    /// <summary>What applying a profile would replace: only entries that really differ (finding 3).</summary>
    private static void Replaced()
    {
        var mine = new SmdContent();
        mine.GetOrAddBuildings("Stove").AddOrReplace(P("Same", "s"), true);
        mine.GetOrAddBuildings("Stove").AddOrReplace(P("Diff", "mine"), false);
        mine.colony = new StoredBlock { config = new XElement("config", "c1") };
        mine.policies = new StoredBlock { config = new XElement("config", "p1"), savedAt = "2026-01-01" };
        var inc = new SmdContent();
        inc.GetOrAddBuildings("Stove").AddOrReplace(new StoredPreset { name = "Same", config = new XElement("config", new XElement("x", "s")), savedAt = "2027-01-01" }, true);
        inc.GetOrAddBuildings("Stove").AddOrReplace(P("Diff", "theirs"), false);
        inc.GetOrAddBuildings("Shelf").AddOrReplace(P("New", "n"), true);
        inc.colony = new StoredBlock { config = new XElement("config", "c2") };
        inc.policies = new StoredBlock { config = new XElement("config", "p1"), savedAt = "2027-01-01" };
        var r = mine.ReplacedBy(inc, ProfileParts.All);
        Check(r.buildings.Count == 1 && r.buildings[0].presets.Count == 1 && r.buildings[0].presets[0].name == "Diff"
            && r.buildings[0].presets[0].config.Element("x").Value == "mine", "only the preset that differs counts as replaced (the old version is returned)");
        Check(r.colony != null && r.policies == null, "a differing colony block counts, an equal policy set (other date) does not");
        Check(mine.ReplacedBy(inc, ProfileParts.Zones).IsEmpty, "parts are honoured");
    }

    /// <summary>A library with policy sets of 15 000 items (several MB): save, load, clone and change stay fast (finding 7).</summary>
    private static void BigLibrary(string root)
    {
        var path = Path.Combine(root, "big", LibraryFile.FileName);
        var cfg = new XElement("config", new XElement("apparel", new XElement("li", new XElement("filter",
            new XElement("allowedDefs", Enumerable.Range(0, 15000).Select(i => new XElement("li", "Def_" + i)))))));
        var big = new SmdLibrary();
        big.content.policies = new StoredBlock { config = cfg };
        for (int i = 0; i < 3; i++)
            big.profiles.Add(new SmdProfile { name = "p" + i, content = new SmdContent { policies = new StoredBlock { config = new XElement(cfg) } } });
        for (int i = 0; i < 60; i++)
            big.content.GetOrAddBuildings("B" + i).AddOrReplace(P("x", "y"), true);
        var d = new LibraryDisk(path);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = d.Commit(big, l => true);
        long save = sw.ElapsedMilliseconds; sw.Restart();
        var loaded = d.Load().library; long load = sw.ElapsedMilliseconds; sw.Restart();
        r = d.Commit(loaded, l => { l.content.GetOrAddBuildings("New").AddOrReplace(P("n", "n"), true); return true; }); long change = sw.ElapsedMilliseconds; sw.Restart();
        bool changed = d.ChangedOnDisk(); long check = sw.ElapsedMilliseconds;
        long kb = new FileInfo(path).Length / 1024;
        Console.WriteLine("     big library: " + kb + " KB, save " + save + " ms, load " + load + " ms, change " + change + " ms, unchanged-check " + check + " ms");
        Check(r.saved && !changed && kb > 1000 && save < 3000 && load < 3000 && change < 3000 && check < 50, "a " + kb + " KB library saves, loads and changes in under 3 s; the idle check is cheap");
    }
}
