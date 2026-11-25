#if JB_TESTS
// In-game suites of the library UI (M6/M7): profile round trip, share-string round trip (also with a missing mod), the first-start
// card (applies its choice exactly once, survives save/load) and the screenshot run of every page and window at three screen sizes.
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
    internal static class U
    {
        /// <summary>A deep copy of the whole library, put back at the end of a suite (the other suites expect what they left).</summary>
        public static SmdLibrary Backup() => SmdLibraryStore.Library.Clone();

        public static void Restore(SmdLibrary b)
        {
            SmdLibraryStore.Mutate(lib =>
            {
                lib.content = b.content.Clone();
                lib.profiles = b.profiles.Select(p => p.Clone()).ToList();
                return true;
            });
        }

        public static string Xml(SmdContent c)
        {
            var e = new XElement("c");
            c?.WriteInto(e);
            return e.ToString(SaveOptions.DisableFormatting);
        }

        public static T NoPause<T>(T w) where T : Window { w.forcePause = false; w.absorbInputAroundWindow = false; return w; }

        /// <summary>A bench preset (one bill) saved under <paramref name="key"/>; the bench is removed again.</summary>
        public static bool BenchPreset(TestContext t, string key, string name, params string[] recipes)
        {
            var b = T.Spawn(t, key);
            if (b == null) return false;
            foreach (var r in recipes) T.AddBill(b, r);
            DefaultBillsController.SaveAsPreset(b, name);
            T.Kill(b);
            return DefaultBillsDatabase.GetStored(key)?.Get(name) != null;
        }

        /// <summary>Adds a bill of a recipe that does not exist and the mod it "needs" to a stored preset.</summary>
        public static bool MakeMissing(string key, string preset)
        {
            return SmdLibraryStore.Mutate(lib =>
            {
                var p = lib.content.Buildings(key)?.Get(preset);
                var bills = p?.config?.Element("bills");
                var li = bills?.Elements("li").FirstOrDefault();
                if (li == null) return false;
                var copy = new XElement(li);
                copy.Element("recipe").Value = "SMDTest_VCE_Stew";
                bills.Add(copy);
                p.requiredMods.Add(new ModRef { id = "smdtest.vcookingexpanded", name = "Vanilla Cooking Expanded (test)" });
                return true;
            });
        }
    }

    // ============================================================================================================ profiles
    /// <summary>smd.profiles: save the current setup as a profile, clear the library, apply the profile → every entry and every
    /// default comes back byte-equal (presets the profile does not name stay); duplicate, rename, delete; the file keeps it.</summary>
    sealed class ProfilesSuite : KitTestSuite
    {
        public override string Name => "smd.profiles";
        public override string Section => SmdSections.Library;
        public override int MaxTicks => 2000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var backup = U.Backup();
            var ps = Find.PlaySettings;
            var before = (ps.autoRebuild, ps.defaultCareForColonist);
            var st = new T.Settings(t.Mod).Section(SmdSections.Library, true).Section(SmdSections.Colony, true).Section(SmdSections.Policies, true);
            const string key = "TableButcher", bench = "SMD P bench", other = "SMD P other", pawnPreset = "SMD P colonist", prof = "SMD Tribal";
            try
            {
                t.Check(U.BenchPreset(t, key, bench, "ButcherCorpseFlesh"), "a bench preset saved");
                var pawn = t.Map.mapPawns.FreeColonists.FirstOrDefault();
                t.Check(pawn != null, "a colonist to save a preset from");
                if (pawn == null) yield break;
                PawnDefaultsController.SaveAsPreset(pawn, pawnPreset);
                PawnDefaultsStore.SetDefaultPreset(pawnPreset);
                ps.autoRebuild = !before.autoRebuild;
                t.Check(ColonySettingsStore.SaveCurrentGame() && PolicyDefaultsStore.SaveCurrentColony(), "colony settings and policy set saved");
                var orig = SmdLibraryStore.Content.Clone();

                SmdLibraryUi.NewProfileName = prof; SmdLibraryUi.NewProfileParts = ProfileParts.All; SmdLibraryUi.NewProfileFromColony = false;
                t.Check(SmdLibraryUi.SaveNewProfile(false), "Save as profile");
                var info = SmdLibraryApi.ListProfiles().FirstOrDefault(p => p.name == prof);
                t.Check(info != null && info.buildingPresets >= 1 && info.pawnPresets >= 1 && info.hasColony && info.hasPolicies,
                    "the profile lists bills, colonist preset, colony settings and policies");
                t.Check(SmdPreview.OneLine(SmdLibraryStore.Library.Profile(prof).content).Length > 10, "one-line preview: " + SmdPreview.OneLine(SmdLibraryStore.Library.Profile(prof).content));
                SmdLibraryUi.NewProfileName = prof;
                t.Check(!SmdLibraryUi.SaveNewProfile(false) && SmdLibraryApi.ListProfiles().Count(p => p.name == prof) == 1, "saving under a taken name asks first (no second profile)");
                SmdLibraryUi.NewProfileName = "";

                // empty the library (another default for the bench to see that the profile's default wins and the other stays)
                DefaultBillsDatabase.ClearAll(key);
                PawnDefaultsStore.RemovePreset(pawnPreset);
                SmdLibraryApi.ClearPart(ProfileParts.Colony | ProfileParts.Policies);
                t.Check(U.BenchPreset(t, key, other, "ButcherCorpseFlesh"), "another preset is now the bench default");
                t.Check(DefaultBillsDatabase.GetStored(key)?.DefaultPreset?.name == other && !ColonySettingsStore.Has() && !PolicyDefaultsStore.HasSet(), "library emptied");

                var lines = SmdLibraryUi.ProfileLines(prof);
                t.Check(lines.Any(l => l.Contains(DefaultBillsDatabase.KeyLabel(key))), "preview names the bench (" + lines.Count + " lines)");

                var res = SmdLibraryUi.ApplyProfileNow(prof);
                t.Check(res.ok && res.merged >= 4, "Apply: ok, " + res.merged + " entries");
                var set = DefaultBillsDatabase.GetStored(key);
                t.Check(set?.DefaultPreset?.name == bench, "the profile's bench preset is the default again (" + set?.DefaultPreset?.name + ")");
                t.Check(set?.Get(other) != null, "the preset the profile does not name is kept");
                t.Check(XNode.DeepEquals(set?.Get(bench)?.config, orig.Buildings(key)?.Get(bench)?.config), "bench preset byte-equal");
                var pw = SmdLibraryStore.Content.pawns;
                t.Check(pw?.DefaultPreset?.name == pawnPreset && XNode.DeepEquals(pw.Get(pawnPreset).config, orig.pawns.Get(pawnPreset).config), "colonist preset back as default, equal");
                t.Check(XNode.DeepEquals(SmdLibraryStore.Content.colony?.config, orig.colony?.config) && XNode.DeepEquals(SmdLibraryStore.Content.policies?.config, orig.policies?.config),
                    "colony settings and policy set equal");
                t.Check(ps.autoRebuild == !before.autoRebuild, "in game: the colony settings were applied to this colony");

                string dup = SmdLibraryUi.DuplicateProfile(prof);
                t.Check(dup == prof + " (2)" && U.Xml(SmdLibraryStore.Library.Profile(dup)?.content) == U.Xml(SmdLibraryStore.Library.Profile(prof).content), "Duplicate: " + dup + ", same content");
                t.Check(SmdLibraryApi.RenameProfile(dup, "SMD Mechanitor run") && SmdLibraryStore.Library.Profile("SMD Mechanitor run") != null, "Rename");
                t.Check(!SmdLibraryApi.RenameProfile("SMD Mechanitor run", prof), "renaming onto a taken name is refused");
                t.Check(SmdLibraryApi.DeleteProfile("SMD Mechanitor run") && SmdLibraryStore.Library.Profile("SMD Mechanitor run") == null, "Delete");

                string xml = U.Xml(SmdLibraryStore.Library.Profile(prof).content);
                SmdLibraryStore.Reload();
                t.Check(U.Xml(SmdLibraryStore.Library.Profile(prof)?.content) == xml, "the profile survives a reload of Library.xml unchanged");
                yield return 1;
            }
            finally
            {
                U.Restore(backup);
                ps.autoRebuild = before.autoRebuild; ps.defaultCareForColonist = before.defaultCareForColonist;
                st.Restore();
            }
        }
    }

    // ============================================================================================================ share strings
    /// <summary>smd.share: copy a profile that needs a missing mod as a share string, paste it: the preview names the missing mod and
    /// nothing is written; import stores a lossless profile (the unknown recipe kept) and never applies it; broken strings are
    /// refused with a readable message; the whole library round-trips too.</summary>
    sealed class ShareSuite : KitTestSuite
    {
        public override string Name => "smd.share";
        public override string Section => SmdSections.Library;
        public override int MaxTicks => 2000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var backup = U.Backup();
            const string key = "TableButcher", preset = "SMD share bench", prof = "SMD Share";
            try
            {
                t.Check(U.BenchPreset(t, key, preset, "ButcherCorpseFlesh") && U.MakeMissing(key, preset), "a bench preset with a bill of a missing mod");
                t.Check(SmdLibraryApi.SaveProfile(prof, ProfileParts.Buildings, null, false, true), "profile saved");
                var original = SmdLibraryStore.Library.Profile(prof);
                t.Check(SmdPreview.MissingMarker(SmdLibraryApi.Info(original).missing)?.Contains("Vanilla Cooking Expanded (test)") == true,
                    "the profile is marked: " + SmdPreview.MissingMarker(SmdLibraryApi.Info(original).missing));

                string s = SmdLibraryUi.CopyProfile(prof);
                t.Check(s != null && s.StartsWith(ShareCodec.Prefix), "share string made (" + (s?.Length ?? 0) + " characters)");
                string clip = SmdUi.Paste();
                t.Check(clip == s, "Copy put it on the clipboard and Paste reads it back");

                string libBefore = SmdLibraryStore.Library.ToXml().ToString();
                string fileBefore = File.Exists(SmdPaths.LibraryFile) ? File.ReadAllText(SmdPaths.LibraryFile) : "";
                SmdLibraryUi.SetImportText(clip);
                var pv = SmdLibraryUi.ImportPreview();
                t.Check(pv.error == null && pv.profile != null && pv.profile.name == prof, "the pasted string decodes: " + (pv.error ?? pv.profile?.name));
                t.Check(pv.missing != null && pv.missing.inactiveMods.Any(m => m.name == "Vanilla Cooking Expanded (test)") && pv.missing.unknownDefs.Any(d => d.Contains("SMDTest_VCE_Stew")),
                    "the preview reports the missing mod and recipe before anything is written");
                t.Check(pv.lines.Any(l => l.Contains(DefaultBillsDatabase.KeyLabel(key))), "the preview lists the bench");
                t.Check(SmdLibraryUi.ImportAsName == prof + " (2)", "a free name is suggested: " + SmdLibraryUi.ImportAsName);
                t.Check(SmdLibraryStore.Library.ToXml().ToString() == libBefore && (File.Exists(SmdPaths.LibraryFile) ? File.ReadAllText(SmdPaths.LibraryFile) : "") == fileBefore,
                    "previewing wrote nothing (library and file unchanged)");

                string contentBefore = U.Xml(SmdLibraryStore.Content);
                string made = SmdLibraryUi.ImportNow();
                t.Check(made == prof + " (2)", "Import as profile: " + made);
                var imp = SmdLibraryStore.Library.Profile(made);
                t.Check(imp != null && U.Xml(imp.content) == U.Xml(original.content), "imported profile equals the original (lossless)");
                t.Check(imp != null && imp.content.Buildings(key)?.Get(preset)?.config.ToString().Contains("SMDTest_VCE_Stew") == true, "the unknown recipe is kept in the import");
                t.Check(U.Xml(SmdLibraryStore.Content) == contentBefore, "importing never applies: the live library is unchanged");

                foreach (var bad in new[] { "hello", "SMD1:abc", s.Substring(0, s.Length / 2) })
                {
                    int n = SmdLibraryStore.Library.profiles.Count;
                    SmdLibraryUi.SetImportText(bad);
                    var b = SmdLibraryUi.ImportPreview();
                    t.Check(b.error != null && b.profile == null && SmdLibraryStore.Library.profiles.Count == n, "refused \"" + bad.Substring(0, Math.Min(12, bad.Length)) + "…\": " + b.error);
                }
                SmdLibraryUi.SetImportText("");

                string all = SmdLibraryUi.CopyLibrary();
                var dec = ShareCodec.Decode(all);
                var expect = SmdLibraryStore.Content.Clone(); expect.RemoveEmpty();
                t.Check(U.Xml(dec.content) == U.Xml(expect), "Copy my whole library round-trips (" + all.Length + " characters)");
                yield return 1;
            }
            finally
            {
                SmdLibraryUi.SetImportText("");
                U.Restore(backup);
            }
        }
    }

    // ============================================================================================================ first-start card
    /// <summary>smd.startcard: a new colony gets the card once; choosing a profile applies it exactly once (presets, colony settings,
    /// colonist preset on the starting colonists); closing again, Due() and a save/load never apply it again; the joined card changes
    /// nothing in the colony.</summary>
    sealed class StartCardSuite : KitTestSuite
    {
        public override string Name => "smd.startcard";
        public override string Section => SmdSections.Library;
        public override int MaxTicks => 150000;

        public override IEnumerable<int> Run(TestContext t)
        {
            // 1. the real path: the new game marked the card due, it opened by itself after the map was shown, the runner closed it
            float until = Time.realtimeSinceStartup + 20f;
            while (SmdGameData.startCard != SmdGameData.StartDone && Time.realtimeSinceStartup < until) yield return 1;
            t.Check(SmdGameData.startCard == SmdGameData.StartDone && SmdGameData.startChoice == "", "the new colony's card opened by itself and was resolved with \"my defaults\" (" + SmdGameData.startCard + ", \"" + SmdGameData.startChoice + "\")");
            t.Check(SmdStartCard.AppliedCount == 1, "applied once so far (" + SmdStartCard.AppliedCount + ")");

            var backup = U.Backup();
            var ps = Find.PlaySettings;
            var before = (ps.autoRebuild, ps.defaultCareForColonist);
            var colonists = t.Map.mapPawns.FreeColonists.ToList();
            var oldPawn = colonists.Select(p => (p, p.playerSettings.hostilityResponse, p.playerSettings.medCare)).ToList();
            var st = new T.Settings(t.Mod).Section(SmdSections.Library, true).Section(SmdSections.Colony, true).Section(SmdSections.Pawns, true)
                .Set("smd.autoApplyToNewPawns", true).Set("smd.pawnApplyHostility", true);
            const string key = "TableButcher", bench = "SMD start bench", prof = "SMD Start";
            Window card = null;
            try
            {
                // the profile: a bench default, a colonist preset that flees, colony settings with auto rebuild flipped
                t.Check(U.BenchPreset(t, key, bench, "ButcherCorpseFlesh"), "bench preset");
                var model = colonists[0];
                model.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                PawnDefaultsController.SaveAsPreset(model, "SMD start colonist");
                PawnDefaultsStore.SetDefaultPreset("SMD start colonist");
                ps.autoRebuild = !before.autoRebuild;
                ColonySettingsStore.SaveCurrentGame();
                t.Check(SmdLibraryApi.SaveProfile(prof, ProfileParts.Buildings | ProfileParts.Pawns | ProfileParts.Colony, null, false, true), "profile saved");
                // back to a plain library and colony, so applying the profile shows
                DefaultBillsDatabase.ClearAll(key); PawnDefaultsStore.RemovePreset("SMD start colonist"); SmdLibraryApi.ClearPart(ProfileParts.Colony);
                ps.autoRebuild = before.autoRebuild;
                foreach (var p in colonists) p.playerSettings.hostilityResponse = HostilityResponseMode.Flee;

                // 2. the card on a new colony, profile chosen
                SmdGameData.startCard = SmdGameData.StartNewColony; SmdGameData.startChoice = null;
                int applied0 = SmdStartCard.AppliedCount;
                SmdStartCard.Begin();
                card = t.OpenWelcomeCard();
                foreach (var y in t.Frames(3)) yield return y;
                t.Check(card != null && card.IsOpen && SmdStartCard.Choice == "", "the framework's welcome card opens with \"Use my defaults\" chosen");
                SmdStartCard.Choice = prof;
                foreach (var y in t.Frames(2)) yield return y;
                card?.Close(false);
                foreach (var y in t.Frames(2)) yield return y;
                t.Check(SmdStartCard.AppliedCount == applied0 + 1 && SmdGameData.startCard == SmdGameData.StartDone && SmdGameData.startChoice == prof, "closing applied the choice once (" + SmdGameData.startChoice + ")");
                t.Check(DefaultBillsDatabase.GetStored(key)?.DefaultPreset?.name == bench, "the profile's bench preset is the default");
                t.Check(ps.autoRebuild == !before.autoRebuild, "the profile's colony settings were applied to this colony");
                t.Check(colonists.All(p => p.playerSettings.hostilityResponse == HostilityResponseMode.Ignore), "the starting colonists got the profile's colonist preset");

                // 3. exactly once: closing again, Apply again
                SmdStartCard.Closed(false);
                t.Check(!SmdStartCard.Apply(prof, true, false) && SmdStartCard.AppliedCount == applied0 + 1, "a second close / Apply does nothing");
                foreach (var p in colonists) p.playerSettings.hostilityResponse = HostilityResponseMode.Flee;
                ps.autoRebuild = before.autoRebuild;

                // 4. save → load keeps "done" (the node is read back the way a load does)
                string name = "jb_smd_startcard";
                GameDataSaveLoader.SaveGame(name);
                yield return 5;
                string path = GenFilePaths.FilePathForSavedGame(name);
                string text = File.Exists(path) ? File.ReadAllText(path) : "";
                var node = text.Length > 0 ? XDocument.Parse(text).Descendants("li").FirstOrDefault(e => (string)e.Element("id") == "juliusberger.workbenchbilldefaults")?.Element("data") : null;
                t.Check(node != null && (string)node.Element("startCard") == "3" && (string)node.Element("startChoice") == prof, "the save holds startCard 3 and the choice");
                if (node != null)
                {
                    int known = SmdGameData.KnownCount;
                    SmdGameData.Reset();
                    string tmp = Path.Combine(GenFilePaths.SaveDataFolderPath, "smd_card_node.xml");
                    File.WriteAllText(tmp, "<root>" + string.Concat(node.Nodes()) + "</root>");
                    Scribe.loader.InitLoading(tmp);
                    try { SmdGameData.Expose(); Scribe.loader.FinalizeLoading(); }
                    catch { Scribe.ForceStop(); throw; }
                    File.Delete(tmp);
                    t.Check(SmdGameData.startCard == SmdGameData.StartDone && SmdGameData.startChoice == prof && SmdGameData.KnownCount == known, "after the load the card is done and the roster intact");
                    t.Check(!SmdStartCard.Apply(prof, false, false), "after the load the choice cannot apply again");
                    t.Check(colonists.All(p => p.playerSettings.hostilityResponse == HostilityResponseMode.Flee) && ps.autoRebuild == before.autoRebuild, "nothing was applied again by the load");
                }
                try { File.Delete(path); } catch { }

                // 5. joined an existing save: only the modes, nothing applied to the colony
                SmdGameData.startCard = SmdGameData.StartJoined; SmdGameData.startChoice = null;
                string lib0 = U.Xml(SmdLibraryStore.Content);
                card = t.OpenWelcomeCard();
                foreach (var y in t.Frames(3)) yield return y;
                card?.Close(false);
                foreach (var y in t.Frames(2)) yield return y;
                t.Check(SmdGameData.startCard == SmdGameData.StartDone && SmdGameData.startChoice == "" && U.Xml(SmdLibraryStore.Content) == lib0 && ps.autoRebuild == before.autoRebuild,
                    "the joined card is done and changed nothing");
                t.Check(!SmdUi.Errors.ContainsKey("card"), "the card drew without error" + (SmdUi.Errors.TryGetValue("card", out var err) ? ": " + err : ""));
            }
            finally
            {
                if (card != null && card.IsOpen) card.Close(false);
                SmdGameData.startCard = SmdGameData.StartDone;
                U.Restore(backup);
                ps.autoRebuild = before.autoRebuild; ps.defaultCareForColonist = before.defaultCareForColonist;
                foreach (var (p, h, m) in oldPawn) { p.playerSettings.hostilityResponse = h; p.playerSettings.medCare = m; }
                st.Restore();
            }
        }
    }

    // ============================================================================================================ screenshots
    /// <summary>smd.ui (explicit): fills the library with a realistic setup (benches, shelf, zones, colonists, policies, colony,
    /// profiles, one preset of a missing mod), then draws every library page in the settings window and in the in-game window, the
    /// share page with a pasted string and both welcome cards at 1280x720, 1920x1080 and 2560x1440 (UI scale 1.5 / 1 / 0.75 on the
    /// 1920x1080 test screen, so the game really lays out for that size). Checks drawn + no exception; JB_UI_SHOTS leaves the PNGs.</summary>
    sealed class UiSuite : KitTestSuite
    {
        public override string Name => "smd.ui";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 60000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var backup = U.Backup();
            var st = new T.Settings(t.Mod).Section(SmdSections.Library, true);
            var made = new List<Zone>();
            var open = new List<Window>();
            IDisposable screen = null;
            try
            {
                // ---- a realistic library
                U.BenchPreset(t, "ElectricStove", "Kitchen (simple meals)", "CookMealSimple", "CookMealFine", "Make_Pemmican");
                U.BenchPreset(t, "ElectricStove", "Feast before the raid", "CookMealLavish");
                U.BenchPreset(t, "TableButcher", "Butcher everything", "ButcherCorpseFlesh");
                U.MakeMissing("TableButcher", "Butcher everything");
                U.BenchPreset(t, "Shelf", "Medicine shelf");
                U.BenchPreset(t, "HydroponicsBasin", "Healroot");
                var sp = BillTemplateSuite.MakeStockpile(t.Map, "SMD UI stockpile");
                if (sp != null) { made.Add(sp); DefaultBillsController.SaveZonePreset(sp, DefaultBillsDatabase.ZoneStockpile, "Food and medicine only"); }
                var colonist = t.Map.mapPawns.FreeColonists.FirstOrDefault();
                if (colonist != null) { PawnDefaultsController.SaveAsPreset(colonist, "Worker"); PawnDefaultsController.SaveAsPreset(colonist, "Night owl (sleeps during the day)"); }
                ColonySettingsStore.SaveCurrentGame();
                PolicyDefaultsStore.SaveCurrentColony();
                SmdLibraryApi.SaveProfile("Tribal start", ProfileParts.All, "Neolithic gear, herbal medicine, everyone hunts.", false, true);
                SmdLibraryApi.SaveProfile("Mechanitor run", ProfileParts.Buildings | ProfileParts.Pawns, null, false, true);
                SmdLibraryApi.SaveProfile("Ein sehr langer Profilname für eine Kolonie mit vielen Mods", ProfileParts.Buildings, null, false, true);
                SmdLibraryUi.NewProfileFromColony = true; SmdLibraryUi.NewProfileParts = ProfileParts.All;
                SmdLibraryUi.Expanded.Add("b:ElectricStove|Kitchen (simple meals)");
                SmdLibraryUi.Expanded.Add("prof:Tribal start");
                string share = SmdLibraryApi.ExportProfile("Tribal start");
                t.Check(SmdLibraryApi.Status().buildingPresets >= 5 && SmdLibraryApi.ListProfiles().Count >= 3, "library filled (" + SmdLibraryApi.Status().buildingPresets + " building presets, " + SmdLibraryApi.ListProfiles().Count + " profiles)");

                Messages.Clear();
                foreach (var y in t.Frames(2)) yield return y;
                var k = t.Mod;
                var sizes = new[] { (1.5f, 1280, 720), (1f, 1920, 1080), (0.75f, 2560, 1440) };
                foreach (var (scale, w, h) in sizes)
                {
                    string tag = w + "x" + h;
                    screen?.Dispose();
                    screen = t.ScreenHeight(h);
                    foreach (var y in t.Frames(4)) yield return y;
                    t.Check(Math.Abs(UI.screenWidth - w) <= 1 && Math.Abs(UI.screenHeight - h) <= 1, tag + ": the game lays out for " + UI.screenWidth + "x" + UI.screenHeight);

                    // settings window pages
                    foreach (var page in new[] { SmdLibraryUi.PageLibrary, SmdLibraryUi.PageProfiles, SmdLibraryUi.PageShare })
                    {
                        SmdLibraryUi.SetImportText(page == SmdLibraryUi.PageShare ? share : "");
                        k.OpenSettings("page:" + page);
                        var dlg = Find.WindowStack.WindowOfType<Dialog_ModSettings>();
                        if (dlg != null) { U.NoPause(dlg); open.Add(dlg); }
                        SmdUi.Draws.TryGetValue(page, out int d0);
                        foreach (var y in t.Frames(4)) yield return y;
                        t.Shot("smd_settings_" + tag + "_" + page);
                        foreach (var y in t.Frames(2)) yield return y;
                        SmdUi.Draws.TryGetValue(page, out int d1);
                        t.Check(dlg != null && d1 > d0 && !SmdUi.Errors.ContainsKey(page), tag + " settings window, " + page + ": drawn " + (d1 - d0) + " frames"
                            + (SmdUi.Errors.TryGetValue(page, out var e) ? ", ERROR " + e : ""));
                        if (dlg != null) t.Check(InsideScreen(dlg.windowRect), tag + " settings window inside the screen " + R(dlg.windowRect));
                        dlg?.Close(false);
                        foreach (var y in t.Frames(1)) yield return y;
                    }

                    // the in-game library window, every tab
                    var win = U.NoPause(new Dialog_SmdLibrary(SmdLibraryUi.PageLibrary));
                    Find.WindowStack.Add(win); open.Add(win);
                    foreach (var tab in new[] { SmdLibraryUi.PageLibrary, SmdLibraryUi.PageProfiles, SmdLibraryUi.PageShare })
                    {
                        SmdLibraryUi.SetImportText(tab == SmdLibraryUi.PageShare ? share : "");
                        win.Tab = tab;
                        SmdUi.Draws.TryGetValue(tab, out int d0);
                        foreach (var y in t.Frames(4)) yield return y;
                        t.Shot("smd_window_" + tag + "_" + tab);
                        foreach (var y in t.Frames(2)) yield return y;
                        SmdUi.Draws.TryGetValue(tab, out int d1);
                        t.Check(win.IsOpen && d1 > d0 && !SmdUi.Errors.ContainsKey(tab) && !SmdUi.Errors.ContainsKey("window"), tag + " library window, " + tab + ": drawn");
                        t.Check(InsideScreen(win.windowRect), tag + " library window inside the screen " + R(win.windowRect));
                    }
                    win.Close(false);
                    SmdLibraryUi.SetImportText("");

                    // the welcome cards
                    foreach (bool joined in new[] { false, true })
                    {
                        int state = SmdGameData.startCard;
                        SmdGameData.startCard = joined ? SmdGameData.StartJoined : SmdGameData.StartNewColony;
                        SmdStartCard.Begin();
                        SmdStartCard.Choice = joined ? "" : "Tribal start";
                        SmdUi.Draws.TryGetValue("card", out int c0);
                        var card = t.OpenWelcomeCard(); if (card != null) open.Add(card);
                        foreach (var y in t.Frames(4)) yield return y;
                        t.Shot("smd_card_" + tag + (joined ? "_joined" : "_new"));
                        foreach (var y in t.Frames(2)) yield return y;
                        SmdUi.Draws.TryGetValue("card", out int c1);
                        t.Check(card != null && card.IsOpen && c1 > c0 && !SmdUi.Errors.ContainsKey("card") && InsideScreen(card.windowRect),
                            tag + " welcome card " + (joined ? "(joined)" : "(new colony)") + " with this mod's part drawn, inside " + (card != null ? R(card.windowRect) : "-"));
                        SmdGameData.startCard = SmdGameData.StartDone; // closing must not apply anything here
                        card?.Close(false);
                        SmdGameData.startCard = state;
                        foreach (var y in t.Frames(1)) yield return y;
                    }
                }
                screen?.Dispose(); screen = null;
                t.Check(SmdUi.Errors.Count == 0, "no page threw (" + string.Join("; ", SmdUi.Errors.Select(x => x.Key + ": " + x.Value)) + ")");
            }
            finally
            {
                foreach (var w in open) if (w.IsOpen) w.Close(false);
                screen?.Dispose();
                SmdLibraryUi.SetImportText("");
                SmdLibraryUi.Expanded.Clear();
                foreach (var z in made) z.Delete();
                U.Restore(backup);
                st.Restore();
            }
        }

        static bool InsideScreen(Rect r) => r.x >= -0.5f && r.y >= -0.5f && r.xMax <= UI.screenWidth + 0.5f && r.yMax <= UI.screenHeight + 0.5f;
        static string R(Rect r) => $"({r.x:0},{r.y:0} {r.width:0}x{r.height:0})";
    }
    // ============================================================================================================ seed for the plain run
    /// <summary>smd.seed (explicit, needs SMD_LIBRARY_DIR): writes a realistic library (bench, shelf and zone presets, a default
    /// colonist preset, colony settings, a policy set with its own default outfit, two profiles) into that folder and leaves it there,
    /// so a later plain run (jbmod plain with the same SMD_LIBRARY_DIR) starts a new colony with defaults to apply.</summary>
    sealed class SeedSuite : KitTestSuite
    {
        public override string Name => "smd.seed";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 2000;

        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMD_LIBRARY_DIR")), "SMD_LIBRARY_DIR is set (never seeds a real library)");
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMD_LIBRARY_DIR"))) yield break;
            t.Check(U.BenchPreset(t, "ElectricStove", "Kitchen", "CookMealSimple", "Make_Pemmican"), "stove preset");
            t.Check(U.BenchPreset(t, "TableButcher", "Butcher", "ButcherCorpseFlesh"), "butcher preset");
            t.Check(U.BenchPreset(t, "Shelf", "Shelf"), "shelf preset");
            var sp = BillTemplateSuite.MakeStockpile(t.Map, "SMD seed");
            if (sp != null) { DefaultBillsController.SaveZonePreset(sp, DefaultBillsDatabase.ZoneStockpile, "Stockpile"); sp.Delete(); }
            var pawn = t.Map.mapPawns.FreeColonists.FirstOrDefault();
            if (pawn != null)
            {
                pawn.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                PawnDefaultsController.SaveAsPreset(pawn, "Worker");
                PawnDefaultsStore.SetDefaultPreset("Worker");
            }
            var ps = Find.PlaySettings;
            ps.autoRebuild = true; ps.defaultCareForPrisoner = MedicalCareCategory.NoMeds;
            t.Check(ColonySettingsStore.SaveCurrentGame(), "colony settings saved");
            var outfit = Current.Game.outfitDatabase.MakeNewOutfit();
            outfit.label = "SMD seed outfit";
            PolicyDefaultsController.SetColonyDefault(PolicyKind.Apparel, outfit);
            t.Check(PolicyDefaultsStore.SaveCurrentColony(), "policy set saved (default outfit \"SMD seed outfit\")");
            t.Check(SmdLibraryApi.SaveProfile("Tribal start", ProfileParts.All, null, false, true) && SmdLibraryApi.SaveProfile("Mechanitor run", ProfileParts.Buildings, null, false, true), "two profiles");
            t.Note("library: " + SmdPaths.LibraryFile + ", " + SmdPreview.OneLine(SmdLibraryStore.Content));
            yield return 1;
        }
    }
}
#endif
