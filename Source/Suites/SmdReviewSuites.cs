#if JB_TESTS
// In-game suites for the defects found by the independent review of 24.11.2026 (each one would have caught its defect):
// smd.librarysafety (two games writing one library, a library of a newer version, a broken Library.xml with "Restore the backup",
// cached previews), smd.cardsafety (welcome card: confirmation before a profile replaces presets, a backup profile of what it
// replaced, the colony gets the profile even when the library cannot be written, the policies box follows the setting) and the
// explicit two-phase smd.oldsave (a save in the pre-2.0 layout keeps its colonist roster).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BetterWorkbenchDefaults.Lib;
using JBFramework;
using JBFramework.Testing;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults.Suites
{
    internal static class R
    {
        /// <summary>Red errors logged while <paramref name="a"/> runs.</summary>
        public static int RedsDuring(Action a)
        {
            int reds = 0;
            Application.LogCallback cb = (m, s, type) => { if (type == LogType.Error || type == LogType.Exception) reds++; };
            Application.logMessageReceivedThreaded += cb;
            try { a(); }
            finally { Application.logMessageReceivedThreaded -= cb; }
            return reds;
        }

        /// <summary>The library files as they are now (put back with <see cref="PutBack"/>).</summary>
        public static Dictionary<string, byte[]> Files()
        {
            var d = new Dictionary<string, byte[]>();
            foreach (var f in new[] { SmdPaths.LibraryFile, SmdPaths.LibraryFile + LibraryFile.BackupSuffix })
                d[f] = File.Exists(f) ? File.ReadAllBytes(f) : null;
            return d;
        }

        public static void PutBack(Dictionary<string, byte[]> files)
        {
            foreach (var kv in files)
            {
                try
                {
                    if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                    else { File.SetAttributes(kv.Key, FileAttributes.Normal); File.WriteAllBytes(kv.Key, kv.Value); }
                }
                catch (Exception e) { Log.Warning("[JBTEST] could not put back " + kv.Key + ": " + e.Message); }
            }
            foreach (var f in Directory.GetFiles(SmdPaths.LibraryFolder, "Library.broken-*.xml").Concat(Directory.GetFiles(SmdPaths.LibraryFolder, "Library.format*.xml")))
                try { File.Delete(f); } catch { }
            try { File.Delete(SmdPaths.LibraryFile + ".lock"); } catch { }
            SmdLibraryStore.Reload();
        }

        public static StoredPreset Preset(string name, string marker) =>
            new StoredPreset { name = name, config = new XElement("config", new XElement("smdTestMarker", marker)) };
    }

    // ============================================================================================================ library file safety
    /// <summary>smd.librarysafety: (1) another game writes the library while this one runs: both games' presets survive and this game
    /// picks up the other's change by itself; (2) a library written by a newer version keeps what this version does not know and gets
    /// a one-time copy; (3) a broken Library.xml: no red error, read-only, the backup is used meanwhile, "Restore the backup" works and
    /// keeps the broken file; (4) previews are cached per library revision (no re-scan every frame).</summary>
    sealed class LibrarySafetySuite : KitTestSuite
    {
        public override string Name => "smd.librarysafety";
        public override string Section => SmdSections.Library;
        public override int MaxTicks => 4000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var backup = U.Backup();
            SmdLibraryStore.Mutate(lib => { lib.content.GetOrAddBuildings("SmdTestSeed").AddOrReplace(R.Preset("seed", "0"), true); return true; });
            var files = R.Files();
            string path = SmdPaths.LibraryFile;
            try
            {
                // ---- 1. two games
                var other = new LibraryDisk(path);
                var theirs = other.Load().library;
                t.Check(theirs != null, "the 'other game' reads the library");
                var c1 = other.Commit(theirs, l => { l.content.GetOrAddBuildings("SmdOtherGame").AddOrReplace(R.Preset("from the other game", "o"), true); return true; });
                t.Check(c1.saved, "the other game saved a preset");
                // this game has not looked at the file since: its next change must not wipe the other game's preset
                bool mine = SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings("SmdThisGame").AddOrReplace(R.Preset("from this game", "t"), true); return true; });
                var disk = LibraryFile.Load(path).library;
                t.Check(mine && disk.content.Buildings("SmdOtherGame")?.Get("from the other game") != null && disk.content.Buildings("SmdThisGame")?.Get("from this game") != null,
                    "both games' presets are in Library.xml after this game saved (no lost update)");
                t.Check(SmdLibraryStore.Content.Buildings("SmdOtherGame") != null, "this game shows the other game's preset");
                var c2 = other.Commit(c1.library, l => { l.content.GetOrAddBuildings("SmdOtherGame").AddOrReplace(R.Preset("second", "o2"), false); return true; });
                int rev = SmdLibraryStore.Revision;
                SmdLibraryStore.RefreshFromDisk(true);
                t.Check(c2.saved && c2.mergedWithDisk && SmdLibraryStore.Content.Buildings("SmdOtherGame")?.Get("second") != null && SmdLibraryStore.Revision > rev,
                    "a change of the other game is read again by itself (revision " + rev + " → " + SmdLibraryStore.Revision + ")");
                yield return 1;

                // ---- 2. a library of a newer version
                var x = LibraryFile.Load(path).library.ToXml();
                x.SetAttributeValue("version", 99);
                x.Add(new XElement("smdFutureSection", new XAttribute("keep", "1")));
                x.Element("buildings")?.Elements("set").FirstOrDefault()?.Add(new XElement("smdFutureChild", "k"));
                x.Element("buildings")?.Elements("set").FirstOrDefault()?.Elements("preset").FirstOrDefault()?.SetAttributeValue("smdFutureAttr", "a");
                File.WriteAllText(path, x.ToString());
                SmdLibraryStore.Reload();
                t.Check(!SmdLibraryStore.ReadOnly && SmdLibraryStore.NewerFormat == 99, "a format-99 library loads (writable; known parts used)");
                bool saved = SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings("SmdThisGame").AddOrReplace(R.Preset("after newer", "n"), false); return true; });
                string text = File.ReadAllText(path);
                t.Check(saved && text.Contains("smdFutureSection") && text.Contains("smdFutureChild") && text.Contains("smdFutureAttr=\"a\"") && text.Contains("version=\"99\""),
                    "after a save by this version the newer version's section, child and attribute are still there");
                t.Check(File.Exists(Path.Combine(SmdPaths.LibraryFolder, "Library.format99.xml")), "a copy Library.format99.xml was kept before the first write");
                R.PutBack(files);
                yield return 1;

                // ---- 3. a broken Library.xml
                SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings("SmdTestSeed").AddOrReplace(R.Preset("seed 2", "1"), false); return true; }); // makes a .bak
                var good = File.ReadAllBytes(path);
                File.WriteAllText(path, "<SetMyDefaultsLibrary version=\"4\"><buildings><set key=");
                var brokenHash = LegacyMigration.Sha256(path);
                int reds = R.RedsDuring(() => SmdLibraryStore.Reload());
                t.Check(reds == 0, "a broken Library.xml logs no red error (" + reds + ")");
                t.Check(SmdLibraryStore.ReadOnly && SmdLibraryStore.UsingBackup && SmdLibraryStore.Content.Buildings("SmdTestSeed") != null,
                    "read-only, and the readable backup is used meanwhile (presets still available)");
                bool refused = !SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings("X").AddOrReplace(R.Preset("x", "x"), true); return true; });
                t.Check(refused && LegacyMigration.Sha256(path) == brokenHash, "a change is refused; the broken file is byte-for-byte untouched");
                t.Check(DefaultBillsController.SaveFailedText().Length > 0, "the refusal explains itself: " + DefaultBillsController.SaveFailedText());
                // the banner with the button, as the player sees it
                var win = U.NoPause(new Dialog_SmdLibrary(SmdLibraryUi.PageLibrary));
                Find.WindowStack.Add(win);
                foreach (var y in U.Frames(4)) yield return y;
                U.Shot("smd_readonly_banner");
                foreach (var y in U.Frames(2)) yield return y;
                t.Check(win.IsOpen && !SmdUi.Errors.ContainsKey(SmdLibraryUi.PageLibrary), "the library page draws the read-only banner");
                win.Close(false);
                string err = SmdLibraryUi.RestoreBackupNow();
                var kept = Directory.GetFiles(SmdPaths.LibraryFolder, "Library.broken-*.xml");
                t.Check(err == null && !SmdLibraryStore.ReadOnly && SmdLibraryStore.Content.Buildings("SmdTestSeed") != null, "\"Restore the backup\" makes the library writable again (" + (err ?? "ok") + ")");
                t.Check(kept.Length == 1 && LegacyMigration.Sha256(kept[0]) == brokenHash, "the broken file is kept as " + (kept.Length > 0 ? Path.GetFileName(kept[0]) : "?"));
                t.Check(SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings("SmdTestSeed").AddOrReplace(R.Preset("seed 3", "3"), false); return true; }), "after the restore a change saves again");
                R.PutBack(files);
                yield return 1;

                // ---- 4. previews are computed once per library revision
                var cont = SmdLibraryStore.Content;
                var m1 = SmdLibraryApi.MissingFor(cont); var m2 = SmdLibraryApi.MissingFor(cont);
                var l1 = SmdPreview.OneLine(cont); var l2 = SmdPreview.OneLine(cont);
                var p1 = SmdLibraryApi.ListProfiles();
                t.Check(ReferenceEquals(m1, m2) && ReferenceEquals(l1, l2), "missing-mod report and one-line preview are cached (same object on the second call)");
                SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings("SmdTestSeed").AddOrReplace(R.Preset("seed 4", "4"), false); return true; });
                t.Check(!ReferenceEquals(SmdLibraryApi.MissingFor(SmdLibraryStore.Content), m1) && SmdPreview.OneLine(SmdLibraryStore.Content) != null, "after a change they are computed again");
                t.Check(p1 != null, "profile list available");
            }
            finally
            {
                R.PutBack(files);
                U.Restore(backup);
            }
        }
    }

    // ============================================================================================================ welcome card safety
    /// <summary>smd.cardsafety: (1) "Also bring my saved policies" starts from "Apply the saved policies to new games"; (2) a profile
    /// that would replace presets of the same name asks first (with the list) and nothing changes until confirmed; after the
    /// confirmation the library's old versions are kept as the profile "Before …"; (3) when the library cannot be written the colony
    /// still gets the profile's colony settings and the message says the library was not updated.</summary>
    sealed class CardSafetySuite : KitTestSuite
    {
        public override string Name => "smd.cardsafety";
        public override string Section => SmdSections.Library;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var backup = U.Backup();
            var files = R.Files();
            int appliedCount0 = SmdStartCard.AppliedCount;
            var ps = Find.PlaySettings;
            bool rebuild0 = ps.autoRebuild;
            int state0 = SmdGameData.startCard; string choice0 = SmdGameData.startChoice;
            bool sup = SmdStartCard.Suppressed;
            var st = new T.Settings(t.Mod).Section(SmdSections.Library, true).Section(SmdSections.Colony, true);
            const string key = "SmdCardKey", name = "SMD card preset", prof = "SMD Card";
            var open = new List<Window>();
            try
            {
                SmdStartCard.Suppressed = true;
                // ---- 1. the policies box
                st.Set("smd.policyAutoApplyOnNewGame", false);
                t.Check(!new Dialog_SmdStartCard(false).AlsoPolicies, "policies box off while \"apply the saved policies to new games\" is off");
                st.Set("smd.policyAutoApplyOnNewGame", true);
                t.Check(new Dialog_SmdStartCard(false).AlsoPolicies, "policies box on while that setting is on");
                st.Set("smd.policyAutoApplyOnNewGame", false);

                // ---- 2. confirmation and backup profile
                SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings(key).AddOrReplace(R.Preset(name, "profile version"), true); return true; });
                t.Check(SmdLibraryApi.SaveProfile(prof, ProfileParts.Buildings, null, false, true), "profile saved");
                SmdLibraryStore.Mutate(l => { l.content.GetOrAddBuildings(key).AddOrReplace(R.Preset(name, "my newer version"), true); return true; });
                SmdGameData.startCard = SmdGameData.StartNewColony; SmdGameData.startChoice = null;
                var card = U.NoPause(SmdStartCard.OpenNow(false)); open.Add(card);
                card.Choice = prof;
                foreach (var y in U.Frames(3)) yield return y;
                t.Check(card.WouldReplace().Count == 1, "the card knows what the profile would replace (" + string.Join(", ", card.WouldReplace()) + ")");
                U.Shot("smd_card_would_replace");
                foreach (var y in U.Frames(2)) yield return y;
                card.TryClose(false);
                // checked in the same frame: the test runner closes message boxes by itself at its next tick
                var box = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().LastOrDefault();
                string boxText = box?.text.ToString() ?? "";
                t.Check(box != null && card.IsOpen && SmdGameData.startCard == SmdGameData.StartNewColony && boxText.Contains(name),
                    "Start asks first (confirmation open and lists the preset, card open, nothing applied): " + boxText.Replace("\n", " "));
                foreach (var y in U.Frames(2)) yield return y;
                t.Check(SmdLibraryStore.Content.Buildings(key)?.Get(name)?.config.Element("smdTestMarker")?.Value == "my newer version", "until confirmed the library keeps my version");
                box?.Close(false);
                card.ReplaceConfirmed = true;   // what the confirmation's OK does
                card.TryClose(false);
                foreach (var y in U.Frames(3)) yield return y;
                string backupName = "WBD_BackupProfileName".Translate(prof).ToString();
                var kept = SmdLibraryStore.Library.Profile(backupName);
                t.Check(!card.IsOpen && SmdGameData.startCard == SmdGameData.StartDone, "after the confirmation the card closed and applied once");
                t.Check(SmdLibraryStore.Content.Buildings(key)?.Get(name)?.config.Element("smdTestMarker")?.Value == "profile version", "the profile's version is in the library now");
                t.Check(kept?.content.Buildings(key)?.Get(name)?.config.Element("smdTestMarker")?.Value == "my newer version", "my replaced version is kept as the profile \"" + backupName + "\"");
                t.Check((SmdStartCard.LastMessage ?? "").Contains(backupName), "the message names the backup profile: " + SmdStartCard.LastMessage);
                var rp = SmdLibraryApi.Preview(prof);
                t.Check(rp != null && rp.wouldReplace.Count == 0, "applying the same profile again would replace nothing (identical entries do not count)");

                // ---- 3. the library cannot be written: the colony still gets the profile
                ps.autoRebuild = !rebuild0;
                ColonySettingsStore.SaveCurrentGame();
                SmdLibraryApi.SaveProfile(prof + " colony", ProfileParts.Colony, null, true, true);
                ps.autoRebuild = rebuild0;
                SmdLibraryApi.ClearPart(ProfileParts.Colony);
                SmdGameData.startCard = SmdGameData.StartNewColony;
                var lockPath = SmdPaths.LibraryFile + ".lock";
                File.WriteAllText(lockPath, "held by a test");   // another game "holds" the library: every write fails after the wait
                bool applied;
                try { applied = SmdStartCard.Apply(prof + " colony", false, false); }
                finally { try { File.Delete(lockPath); } catch { } }
                t.Check(applied && ps.autoRebuild == !rebuild0, "the colony got the profile's colony settings although the library could not be written");
                t.Check(!ColonySettingsStore.Has(), "the library itself was not changed");
                string notSaved = "WBD_CardLibraryNotSaved".Translate("").ToString();
                string probe = notSaved.Length > 20 ? notSaved.Substring(0, 20) : notSaved;
                t.Check((SmdStartCard.LastMessage ?? "").Contains(probe), "the message says the library was not updated: " + SmdStartCard.LastMessage);
            }
            finally
            {
                foreach (var w in open) if (w.IsOpen) { SmdGameData.startCard = SmdGameData.StartDone; w.Close(false); }
                foreach (var w in Find.WindowStack.Windows.OfType<Dialog_MessageBox>().ToList()) w.Close(false);
                ps.autoRebuild = rebuild0;
                SmdGameData.startCard = state0; SmdGameData.startChoice = choice0;
                SmdStartCard.Suppressed = sup;
                SmdStartCard.AppliedCount = appliedCount0;   // smd.startcard counts the applies of the real path
                st.Restore();
                R.PutBack(files);
                U.Restore(backup);
            }
        }
    }

    // ============================================================================================================ zone button
    /// <summary>smd.zonebutton: the zone Defaults button has its own switch; the buildings' switch does not hide it.</summary>
    sealed class ZoneButtonSuite : KitTestSuite
    {
        public override string Name => "smd.zonebutton";
        public override string Section => SmdSections.Zones;
        public override int MaxTicks => 600;

        public override IEnumerable<int> Run(TestContext t)
        {
            var st = new T.Settings(t.Mod).Section(SmdSections.Zones, true).Set("smd.zoneShowGizmo", true).Set("smd.showGizmo", true);
            Zone_Stockpile z = null;
            try
            {
                z = BillTemplateSuite.MakeStockpile(t.Map, "SMD zone button");
                t.Check(z != null, "a stockpile");
                if (z == null) yield break;
                bool Has() => z.GetGizmos().OfType<Command>().Any(c => c.defaultLabel == "WBD_Defaults".Translate().ToString());
                t.Check(Has(), "zone button on");
                st.Set("smd.showGizmo", false);
                t.Check(Has(), "the buildings' button switch off: the zone button stays");
                st.Set("smd.zoneShowGizmo", false);
                t.Check(!Has(), "the zone button switch off: gone");
                yield return 1;
            }
            finally { z?.Delete(); st.Restore(); }
        }
    }

    // ============================================================================================================ old save
    /// <summary>
    /// smd.oldsave (explicit, two phases: jbmod BetterWorkbenchDefaults _two smd_oldsave smd.oldsave --): phase 1 writes a save in the
    /// layout of the previous version (no JB Framework component; the old PawnDefaultsGameComponent node says colonist X got the
    /// colonist preset but not yet work priorities, like a child). Phase 2 loads it: the roster must be carried over (it used to be wiped
    /// by the framework's SaveData reset), and X must get the preset's work priorities from the retry scan.
    /// </summary>
    sealed class OldSaveSuite : KitTestSuite
    {
        public override string Name => "smd.oldsave";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override int MaxTicks => 3000;
        const string SaveName = "jb_smd_oldsave";
        static int reds;
        static void Count(string m, string st, LogType type) { if (type == LogType.Error || type == LogType.Exception) reds++; }

        public override IEnumerable<int> Run(TestContext t)
        {
            string loaded = Environment.GetEnvironmentVariable("JB_TEST_LOAD");
            if (string.IsNullOrEmpty(loaded)) { foreach (var y in Phase1(t)) yield return y; yield break; }
            t.Note("phase 2: loaded " + loaded);
            var comp = Current.Game.GetComponent<PawnDefaultsGameComponent>();
            var sideFile = Path.Combine(GenFilePaths.SavedGamesFolderPath, SaveName + ".smdx.txt");
            int x = -1;
            try { x = int.Parse(File.ReadAllText(sideFile).Trim()); } catch (Exception e) { t.Note("side file: " + e.Message); }
            var pawn = PawnsFinder.AllMaps_FreeColonists.FirstOrDefault(p => p.thingIDNumber == x);
            t.Check(pawn != null, "colonist X (" + x + ") is in the loaded colony");
            if (pawn == null) yield break;
            t.Check(comp.IsSeen(x) && comp.CoreApplied(x) && !comp.WorkApplied(x), "the old roster was carried over: X is seen and defaulted, work still pending");
            t.Check(comp.IsKnown(x) && comp.BaselineTaken, "X is known, the baseline is taken");
            // a colonist preset with a distinctive work priority; X can work now (the child grew up)
            var model = PawnsFinder.AllMaps_FreeColonists.FirstOrDefault(p => p != pawn);
            var wt = DefDatabase<WorkTypeDef>.AllDefsListForReading.FirstOrDefault(w => !pawn.WorkTypeIsDisabled(w) && model != null && !model.WorkTypeIsDisabled(w));
            t.Check(model != null && wt != null, "a model colonist and a work type both can do (" + wt?.defName + ")");
            if (model == null || wt == null) yield break;
            var st = new T.Settings(t.Mod).Section(SmdSections.Pawns, true).Set("smd.autoApplyToNewPawns", true).Set("smd.pawnApplyWork", true);
            try
            {
                Find.PlaySettings.useWorkPriorities = true;
                model.workSettings.SetPriority(wt, 1);
                pawn.workSettings.SetPriority(wt, 3);
                PawnDefaultsController.SaveAsPreset(model, "SMD old save worker");
                PawnDefaultsStore.SetDefaultPreset("SMD old save worker");
                Application.logMessageReceivedThreaded += Count;
                yield return 600;
                t.Check(comp.WorkApplied(x) && pawn.workSettings.GetPriority(wt) == 1, "the retry scan gave X the preset's work priorities (" + wt.defName + " = " + pawn.workSettings.GetPriority(wt) + ")");
                t.Check(reds == 0, "600 ticks played on the old save without a red error (" + reds + "; load-time errors: see the run report)");
            }
            finally
            {
                Application.logMessageReceivedThreaded -= Count;
                PawnDefaultsStore.RemovePreset("SMD old save worker");
                st.Restore();
            }
        }

        IEnumerable<int> Phase1(TestContext t)
        {
            var colonists = PawnsFinder.AllMaps_FreeColonists.ToList();
            t.Check(colonists.Count >= 2, "two colonists");
            if (colonists.Count < 2) yield break;
            int x = colonists[0].thingIDNumber;
            GameDataSaveLoader.SaveGame(SaveName);
            yield return 5;
            string path = GenFilePaths.FilePathForSavedGame(SaveName);
            var doc = XDocument.Load(path);
            var comps = doc.Root.Element("game")?.Element("components");
            t.Check(comps != null, "the save has game components");
            if (comps == null) yield break;
            string fw = typeof(FrameworkGame).FullName;
            int removed = comps.Elements("li").Count(e => (string)e.Attribute("Class") == fw);
            comps.Elements("li").Where(e => (string)e.Attribute("Class") == fw).Remove();
            // the node the previous version wrote (1.x layout: seen / appliedCore / appliedWork, no roster version)
            XElement L(string n, IEnumerable<int> ids) => new XElement(n, ids.Select(i => new XElement("li", i)));
            comps.Add(new XElement("li", new XAttribute("Class", typeof(PawnDefaultsGameComponent).FullName),
                L("seen", new[] { x }), L("appliedCore", new[] { x }), new XElement("appliedWork")));
            doc.Save(path);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), SaveName + ".smdx.txt"), x.ToString());
            string text = File.ReadAllText(path);
            t.Check(removed == 1 && !text.Contains("\"" + fw + "\"") && text.Contains(typeof(PawnDefaultsGameComponent).FullName),
                "the save now has the previous version's layout (framework component removed, old roster node of X = " + x + ")");
        }
    }
}
#endif
