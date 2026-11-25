#if JB_TESTS
// In-game suites of Set my Defaults (compiled only in test builds: jbmod … test). They turn the in-game checks owed by the
// handoff (M1–M10, F-1, NBT-1, S-1, R-1) into automatic checks. Every suite cleans up what it saved in the library and puts
// the settings back. The library of a test run lives in the slot's fake HOME (never the player's).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BetterWorkbenchDefaults.Lib;
using HarmonyLib;
using JBFramework;
using JBFramework.Testing;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace BetterWorkbenchDefaults.Suites
{
    internal static class T
    {
        /// <summary>A free rect whose cells are standable, unzoned and (optionally) fertile; different on every call.</summary>
        public static bool FreeZoneRect(Map map, int w, int h, bool fertile, out CellRect rect)
        {
            var anchor = TestCells.Anchor(map);
            for (int i = 0; i < GenRadial.NumCellsInRadius(45f); i += 3)
            {
                var c = anchor + GenRadial.RadialPattern[i];
                var r = CellRect.CenteredOn(c, w / 2, h / 2);
                if (r.Cells.All(x => x.InBounds(map) && x.Standable(map) && !x.Fogged(map) && x.GetZone(map) == null && x.GetFirstBuilding(map) == null
                        && (!fertile || x.GetTerrain(map).fertility > 0.5f)))
                { rect = r; return true; }
            }
            rect = default; return false;
        }

        public static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamedSilentFail(name);

        /// <summary>A finished player building, spawned like a new one (SpawnSetup without respawningAfterLoad).</summary>
        public static Building Spawn(TestContext t, string defName, IntVec3? at = null)
        {
            var def = Def(defName);
            if (def == null) { t.Check(false, "def " + defName + " exists"); return null; }
            var size = def.Size;
            IntVec3 cell;
            if (at.HasValue) cell = at.Value;
            else
            {
                if (!TestCells.TryFindFreeRect(t.Map, size.x + 2, size.z + 2, out var rect)) { t.Check(false, "free room for " + defName); return null; }
                cell = rect.CenterCell;
            }
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            thing.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, cell, t.Map) as Building;
        }

        public static void Kill(params Thing[] things)
        {
            foreach (var th in things) if (th != null && !th.Destroyed) th.Destroy();
        }

        public static RecipeDef Recipe(string name) => DefDatabase<RecipeDef>.GetNamedSilentFail(name);

        public static Bill_Production AddBill(Building b, string recipe)
        {
            var r = Recipe(recipe);
            if (r == null || !(b is IBillGiver g)) return null;
            var bill = (Bill_Production)r.MakeNewBill();
            g.BillStack.AddBill(bill);
            return bill;
        }

        public static int Bills(Thing b) => (b as IBillGiver)?.BillStack?.Count ?? -1;

        /// <summary>Remembers setting values and puts them back.</summary>
        public sealed class Settings
        {
            readonly KitMod k; readonly Dictionary<string, float> old = new Dictionary<string, float>();
            readonly Dictionary<string, bool> sec = new Dictionary<string, bool>();
            public Settings(KitMod k) { this.k = k; }
            public Settings Set(string key, bool v) { if (!old.ContainsKey(key)) old[key] = k.Get(key); k.Set(key, v); return this; }
            public Settings Set(string key, float v) { if (!old.ContainsKey(key)) old[key] = k.Get(key); k.Set(key, v); return this; }
            public Settings Section(string id, bool on) { if (!sec.ContainsKey(id)) sec[id] = k.Current.SectionOn(id); k.Current.SetSection(id, on); return this; }
            public void Restore() { foreach (var kv in old) k.Set(kv.Key, kv.Value); foreach (var kv in sec) k.Current.SetSection(kv.Key, kv.Value); }
        }

        public static string Allowed(ThingFilter f, params string[] defs) =>
            string.Join(", ", defs.Select(d => d + "=" + (Def(d) != null && f != null && f.Allows(Def(d)))));
    }

    // ============================================================================================================ M1 roster
    /// <summary>smd.roster (M1-1, M1-2, M1-3, M1-4): existing colonists are never re-defaulted when they come back from a caravan,
    /// transport pods or a cryptosleep casket; a new colonist gets the default once.</summary>
    sealed class RosterSuite : KitTestSuite
    {
        public override string Name => "smd.roster";
        public override string Section => SmdSections.Pawns;
        public override int MaxTicks => 6000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var comp = SmdRoster.Current;
            t.Check(comp != null && comp.BaselineTaken, "the roster baseline is taken in a new game");
            var colonists = map.mapPawns.FreeColonists.ToList();
            t.Check(colonists.Count >= 2 && colonists.All(p => comp.IsKnown(p.thingIDNumber)), "every starting colonist is known (" + colonists.Count + ", known " + SmdGameData.KnownCount + ")");
            if (colonists.Count < 2) yield break;

            var st = new T.Settings(t.Mod).Set("smd.autoApplyToNewPawns", true).Section(SmdSections.Pawns, true);
            var model = colonists[0];
            var a = colonists[1];
            var oldModel = (model.playerSettings.hostilityResponse, model.playerSettings.medCare);
            string preset = "SMD test colonist";
            Building casket = null;
            Pawn joiner = null;
            try
            {
                // preset: Flee + no medicine (distinctive)
                model.playerSettings.hostilityResponse = HostilityResponseMode.Flee;
                model.playerSettings.medCare = MedicalCareCategory.NoMeds;
                var snap = PawnConfigSnapshot.Capture(model);
                t.Check(snap != null && PawnDefaultsStore.SavePreset(preset, snap, true) && PawnDefaultsStore.SetDefaultPreset(preset), "colonist preset saved as default");
                a.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
                a.playerSettings.medCare = MedicalCareCategory.Best;
                // "Not defaulted": the preset's Flee / no-medicine never reached A (vanilla itself resets medical care when a pawn passes through the world)
                bool Untouched() => a.playerSettings.hostilityResponse == HostilityResponseMode.Attack && a.playerSettings.medCare != MedicalCareCategory.NoMeds && !comp.CoreApplied(a.thingIDNumber);

                // caravan: leave the map into the world, come back
                var cell = a.Position;
                a.DeSpawn();
                Find.WorldPawns.PassToWorld(a, PawnDiscardDecideMode.KeepForever);
                yield return 30;
                Find.WorldPawns.RemovePawn(a);
                GenSpawn.Spawn(a, cell, map);
                yield return 260; // the retry scan runs every 250 ticks
                t.Check(Untouched(), "caravan: returning colonist not defaulted (" + a.playerSettings.hostilityResponse + ", " + a.playerSettings.medCare + ")");

                // transport pods: a real drop pod lands the colonist
                a.DeSpawn();
                DropPodUtility.DropThingsNear(cell, map, new Thing[] { a }, 30, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: false, forbid: false);
                int waited = 0;
                while (!a.Spawned && waited < 1500) { yield return 50; waited += 50; }
                t.Check(a.Spawned, "transport pod: the colonist landed (" + waited + " ticks)");
                yield return 260;
                t.Check(Untouched(), "transport pod: landed colonist not defaulted (" + a.playerSettings.hostilityResponse + ", " + a.playerSettings.medCare + ")");

                // cryptosleep casket: in and out
                casket = T.Spawn(t, "CryptosleepCasket");
                if (casket is Building_CryptosleepCasket cc)
                {
                    a.DeSpawn();
                    t.Check(cc.TryAcceptThing(a, false), "cryptosleep: colonist went in");
                    yield return 30;
                    cc.EjectContents();
                    yield return 260;
                    t.Check(a.Spawned && Untouched(), "cryptosleep: ejected colonist not defaulted (" + a.playerSettings.hostilityResponse + ", " + a.playerSettings.medCare + ")");
                }
                else t.Note("no cryptosleep casket def (skipped)");
                t.Check(!comp.CoreApplied(a.thingIDNumber), "the existing colonist was never defaulted");

                // a new colonist joins: the default applies once
                joiner = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, mustBeCapableOfViolence: true, developmentalStages: DevelopmentalStage.Adult));
                joiner.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
                GenSpawn.Spawn(joiner, t.FreeCellNearCenter(), map);
                yield return 10;
                t.Check(joiner.playerSettings.hostilityResponse == HostilityResponseMode.Flee && joiner.playerSettings.medCare == MedicalCareCategory.NoMeds,
                    "new colonist got the default (" + joiner.playerSettings.hostilityResponse + ", " + joiner.playerSettings.medCare + ")");
                t.Check(comp.IsKnown(joiner.thingIDNumber) && comp.CoreApplied(joiner.thingIDNumber), "new colonist is known and marked defaulted");
                joiner.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
                var jc = joiner.Position;
                joiner.DeSpawn();
                yield return 10;
                GenSpawn.Spawn(joiner, jc, map);
                yield return 260;
                t.Check(joiner.playerSettings.hostilityResponse == HostilityResponseMode.Attack, "the default is applied only once (respawned joiner keeps the player's change)");

                // M1-5: auto-apply off while a colonist travels, then on again: still unchanged
                t.Mod.Set("smd.autoApplyToNewPawns", false);
                a.DeSpawn(); yield return 10; GenSpawn.Spawn(a, cell, map); yield return 10;
                t.Mod.Set("smd.autoApplyToNewPawns", true);
                a.DeSpawn(); yield return 10; GenSpawn.Spawn(a, cell, map); yield return 260;
                t.Check(Untouched(), "auto-apply off then on: returning colonist still not defaulted");
            }
            finally
            {
                PawnDefaultsStore.RemovePreset(preset);
                model.playerSettings.hostilityResponse = oldModel.hostilityResponse;
                model.playerSettings.medCare = oldModel.medCare;
                T.Kill(casket);
                if (joiner != null && !joiner.Destroyed) joiner.Destroy();
                st.Restore();
            }
        }
    }

    // ============================================================================================================ M2 multi-select
    /// <summary>smd.multiselect (M2-1, M2-2): Apply acts on every selected building of the same def and on every selected zone.</summary>
    sealed class MultiSelectSuite : KitTestSuite
    {
        public override string Name => "smd.multiselect";
        public override string Section => SmdSections.Buildings;
        public override int MaxTicks => 2000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var shelves = new List<Building>();
            var zones = new List<Zone_Growing>();
            string preset = "SMD test multi";
            try
            {
                for (int i = 0; i < 5; i++) { var s = T.Spawn(t, "Shelf"); if (s != null) shelves.Add(s); }
                t.Check(shelves.Count == 5, "5 shelves spawned");
                if (shelves.Count < 5) yield break;
                var model = (IStoreSettingsParent)shelves[0];
                model.GetStoreSettings().filter.SetDisallowAll();
                model.GetStoreSettings().filter.SetAllow(ThingDefOf.Steel, true);
                DefaultBillsController.SaveAsPreset(shelves[0], preset);
                t.Check(DefaultBillsDatabase.GetPresets("Shelf")?.Get(preset) != null, "shelf preset saved");
                for (int i = 1; i < 5; i++) ((IStoreSettingsParent)shelves[i]).GetStoreSettings().filter.SetAllowAll(null);
                Find.Selector.ClearSelection();
                foreach (var s in shelves) Find.Selector.Select(s, false, false);
                DefaultBillsController.ApplyPreset(shelves[1], "Shelf", preset);
                int ok = shelves.Count(s => { var f = ((IStoreSettingsParent)s).GetStoreSettings().filter; return f.Allows(ThingDefOf.Steel) && !f.Allows(ThingDefOf.WoodLog); });
                t.Check(ok == 5, "applying to one selected shelf configured all 5 (" + ok + ")");

                // zones
                for (int i = 0; i < 3; i++)
                {
                    if (!T.FreeZoneRect(map, 3, 3, true, out var rect)) break;
                    var z = new Zone_Growing(map.zoneManager);
                    map.zoneManager.RegisterZone(z);
                    foreach (var c in rect.Cells) if (c.GetZone(map) == null && c.GetTerrain(map).fertility > 0f) z.AddCell(c);
                    if (z.Cells.Count == 0) { z.Delete(); continue; }
                    zones.Add(z);
                }
                t.Check(zones.Count == 3, "3 growing zones created (" + zones.Count + ")");
                if (zones.Count < 3) yield break;
                var rice = T.Def("Plant_Rice");
                zones[0].SetPlantDefToGrow(rice);
                zones[0].allowSow = false;
                DefaultBillsController.SaveZonePreset(zones[0], DefaultBillsDatabase.ZoneGrowing, preset);
                zones[1].allowSow = true; zones[2].allowSow = true;
                Find.Selector.ClearSelection();
                foreach (var z in zones) Find.Selector.Select(z, false, false);
                DefaultBillsController.ApplyZonePreset(zones[2], DefaultBillsDatabase.ZoneGrowing, preset);
                int zok = zones.Count(z => z.GetPlantDefToGrow() == rice && !z.allowSow);
                t.Check(zok == 3, "applying to one selected zone configured all 3 (rice, sow off: " + zok + ")");
                yield return 1;
            }
            finally
            {
                Find.Selector.ClearSelection();
                DefaultBillsDatabase.ClearAll("Shelf");
                DefaultBillsDatabase.ClearAll(DefaultBillsDatabase.ZoneGrowing);
                T.Kill(shelves.ToArray());
                foreach (var z in zones) if (z.zoneManager != null) z.Delete();
            }
        }
    }

    // ============================================================================================================ M3 bill cap
    /// <summary>smd.billcap (M3-1): the bill limit follows the setting at once (no restart), the transpiler is in place and
    /// restoring a preset respects the limit.</summary>
    sealed class BillCapSuite : KitTestSuite
    {
        public override string Name => "smd.billcap";
        public override int MaxTicks => 1000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var st = new T.Settings(t.Mod);
            Building bench = null, bench2 = null;
            string preset = "SMD test cap";
            try
            {
                if (BillCapCompat.Installed)
                {
                    // No Max Bills in the run: either it works (it owns the limit) or it failed to patch (its 1.6 build on 1.7)
                    t.Note("No Max Bills: " + BillCapCompat.State);
                    if (BillCapCompat.Active) { t.Check(!WBD_Mod.BillCapPatched, "No Max Bills works: no transpiler of ours"); yield break; }
                    t.Check(BillCapCompat.Broken && WBD_Mod.BillCapPatched, "No Max Bills is installed but patched nothing: our transpiler took over");
                    st.Set("smd.nativeRaiseBillCap", false).Set("smd.maxBillsCap", 40f);
                    t.Check(BillCap.Current() == 40 && WBD_Mod.Settings.EffectiveBillCap() == 40, "the limit is lifted to \"Bills per workbench\" although our own switch is off (" + BillCap.Current() + ")");
                    st.Restore();
                    yield break;
                }
                t.Check(!BillCapCompat.Active, "No Max Bills is not loaded in this run");
                t.Check(WBD_Mod.BillCapPatched, "the bill-limit transpiler is in place");
                var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(BillStack), nameof(BillStack.DoListing)));
                t.Check(info != null && info.Transpilers.Any(p => p.PatchMethod.DeclaringType == typeof(Patch_BillStack_RaiseCap)), "BillStack.DoListing carries our transpiler");
                st.Set("smd.nativeRaiseBillCap", false);
                t.Check(BillCap.Current() == 15 && WBD_Mod.Settings.EffectiveBillCap() == 15, "off: limit 15 (" + BillCap.Current() + ")");
                st.Set("smd.nativeRaiseBillCap", true).Set("smd.maxBillsCap", 30f);
                t.Check(BillCap.Current() == 30, "on, 30: limit 30 at once (" + BillCap.Current() + ")");
                st.Set("smd.maxBillsCap", 400f);
                t.Check(BillCap.Current() == 250, "typed 400 is held at 250 (" + BillCap.Current() + ")");
                st.Set("smd.maxBillsCap", 30f);

                bench = T.Spawn(t, "TableButcher");
                for (int i = 0; i < 20; i++) T.AddBill(bench, "ButcherCorpseFlesh");
                t.Check(T.Bills(bench) == 20, "20 bills on a bench while the limit is 30");
                DefaultBillsController.SaveAsPreset(bench, preset);
                bench2 = T.Spawn(t, "TableButcher");
                yield return 1;
                t.Check(T.Bills(bench2) == 20, "a new bench gets all 20 default bills under limit 30 (" + T.Bills(bench2) + ")");
                st.Set("smd.nativeRaiseBillCap", false);
                T.Kill(bench2);
                bench2 = T.Spawn(t, "TableButcher");
                yield return 1;
                t.Check(T.Bills(bench2) == 15, "limit switched off: a new bench gets 15 at once, no restart (" + T.Bills(bench2) + ")");
            }
            finally
            {
                DefaultBillsDatabase.ClearAll("TableButcher");
                T.Kill(bench, bench2);
                st.Restore();
            }
        }
    }

    // ============================================================================================================ M4 unknown items
    /// <summary>smd.missing (M4-2): a preset with a recipe of a mod that is not loaded loads without red errors, names the mod,
    /// applies the rest, and survives saving another preset and a reload of the library file.</summary>
    sealed class MissingSuite : KitTestSuite
    {
        public override string Name => "smd.missing";
        public override string Section => SmdSections.Buildings;
        public override int MaxTicks => 1000;

        public override IEnumerable<int> Run(TestContext t)
        {
            Building bench = null, bench2 = null;
            const string key = "TableButcher", preset = "SMD test missing", unknown = "SMDTest_UnknownRecipe";
            try
            {
                bench = T.Spawn(t, key);
                T.AddBill(bench, "ButcherCorpseFlesh");
                DefaultBillsController.SaveAsPreset(bench, preset);
                bool edited = SmdLibraryStore.Mutate(lib =>
                {
                    var p = lib.content.Buildings(key)?.Get(preset);
                    var bills = p?.config?.Element("bills");
                    var li = bills?.Elements("li").FirstOrDefault();
                    if (li == null) return false;
                    var copy = new XElement(li);
                    copy.Element("recipe").Value = unknown;
                    bills.Add(copy);
                    p.requiredMods.Add(new ModRef { id = "smdtest.missingmod", name = "Missing Test Mod" });
                    return true;
                });
                t.Check(edited, "a bill of a missing mod was written into the stored preset");
                var stored = DefaultBillsDatabase.GetStored(key)?.Get(preset);
                var miss = SmdDefRefs.Missing(stored);
                t.Check(miss.inactiveMods.Any(m => m.name == "Missing Test Mod") && miss.unknownDefs.Any(d => d.Contains(unknown)),
                    "the missing mod and recipe are named: " + miss.Summary() + " / " + string.Join(", ", miss.unknownDefs));
                T.Kill(bench); bench = null;
                bench2 = T.Spawn(t, key);
                yield return 1;
                t.Check(T.Bills(bench2) == 1, "the known bill was applied, the unknown one skipped (" + T.Bills(bench2) + " bill)");
                var cfg = DefaultBillsDatabase.GetPresets(key)?.Get(preset)?.config;
                t.Check(cfg != null && cfg.MissingRecipeCount() == 1, "the apply path counts 1 missing recipe");
                DefaultBillsController.SaveAsPreset(bench2, "SMD test other");
                SmdLibraryStore.Reload();
                var again = DefaultBillsDatabase.GetStored(key)?.Get(preset);
                t.Check(again != null && again.config.ToString().Contains(unknown), "after saving another preset and reloading the file, the unknown recipe is still stored");
                t.Check(File.ReadAllText(SmdPaths.LibraryFile).Contains(unknown), "Library.xml on disk still holds it");
            }
            finally
            {
                DefaultBillsDatabase.ClearAll(key);
                T.Kill(bench, bench2);
            }
        }
    }

    // ============================================================================================================ M5 bill template
    /// <summary>smd.billtemplate (M5-1, audit smd.bills.roundtrip): every field of a bill comes back on a new bench, including custom
    /// name and drop-in / count-in stockpiles by name; without stockpiles of that name the bill falls back without error.</summary>
    sealed class BillTemplateSuite : KitTestSuite
    {
        public override string Name => "smd.billtemplate";
        public override string Section => SmdSections.Buildings;
        public override int MaxTicks => 1000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            Building bench = null, bench2 = null;
            Zone_Stockpile store = null, count = null;
            const string key = "TableButcher", preset = "SMD test template";
            try
            {
                store = MakeStockpile(map, "SMD Store"); count = MakeStockpile(map, "SMD Count");
                t.Check(store != null && count != null, "two named stockpiles");
                if (store == null || count == null) yield break;
                bench = T.Spawn(t, key);
                var b = T.AddBill(bench, "ButcherCorpseFlesh");
                b.RenamableLabel = "SMD custom name";
                b.repeatMode = BillRepeatModeDefOf.TargetCount; b.targetCount = 23; b.pauseWhenSatisfied = true; b.unpauseWhenYouHave = 7;
                b.ingredientSearchRadius = 31f; b.allowedSkillRange = new IntRange(3, 12); b.suspended = true;
                b.includeEquipped = true; b.includeTainted = true; b.hpRange = new FloatRange(0.2f, 0.9f);
                b.SetStoreMode(BillStoreModeDefOf.SpecificStockpile, store.slotGroup);
                b.SetIncludeGroup(count.slotGroup);
                // Ideology: a specific (not the ideoligion's) style is saved and restored
                var style = ModsConfig.IdeologyActive ? DefDatabase<ThingStyleDef>.AllDefsListForReading.FirstOrDefault() : null;
                if (style != null) { b.globalStyle = false; b.style = style; }
                DefaultBillsController.SaveAsPreset(bench, preset);
                bench2 = T.Spawn(t, key);
                yield return 1;
                var c = (bench2 as IBillGiver)?.BillStack?.Bills.OfType<Bill_Production>().FirstOrDefault();
                t.Check(c != null, "the new bench got the bill");
                if (c == null) yield break;
                t.Check(c.RenamableLabel == "SMD custom name", "custom name: " + c.RenamableLabel);
                t.Check(c.repeatMode == BillRepeatModeDefOf.TargetCount && c.targetCount == 23 && c.pauseWhenSatisfied && c.unpauseWhenYouHave == 7, "repeat mode, target, pause/unpause");
                t.Check(Math.Abs(c.ingredientSearchRadius - 31f) < 0.01f && c.allowedSkillRange == new IntRange(3, 12), "radius and skill range");
                t.Check(c.suspended && c.includeEquipped && c.includeTainted && Math.Abs(c.hpRange.min - 0.2f) < 0.01f, "suspended, count equipped/tainted, hit points");
                t.Check(c.GetStoreMode() == BillStoreModeDefOf.SpecificStockpile && c.GetSlotGroup() == store.slotGroup, "drop in: " + (c.GetSlotGroup()?.GetType().Name ?? "none") + " (" + c.GetStoreMode()?.defName + ")");
                t.Check(c.GetIncludeSlotGroup() == count.slotGroup, "count in: the stockpile of that name");
                if (style != null) t.Check(!c.globalStyle && c.style == style, "Ideology: the bill's own style came back (" + c.style?.defName + ", global " + c.globalStyle + ")");
                else t.Note("Ideology not active: bill style not checked");

                // without the stockpiles: vanilla fallbacks, no error
                store.Delete(); count.Delete(); store = null; count = null;
                T.Kill(bench2);
                bench2 = T.Spawn(t, key);
                yield return 1;
                var d = (bench2 as IBillGiver)?.BillStack?.Bills.OfType<Bill_Production>().FirstOrDefault();
                t.Check(d != null && d.GetStoreMode() == BillStoreModeDefOf.BestStockpile && d.GetIncludeSlotGroup() == null, "no stockpile of that name: best stockpile, count everywhere");
            }
            finally
            {
                DefaultBillsDatabase.ClearAll(key);
                T.Kill(bench, bench2);
                store?.Delete(); count?.Delete();
            }
        }

        internal static Zone_Stockpile MakeStockpile(Map map, string label)
        {
            if (!T.FreeZoneRect(map, 2, 2, false, out var rect)) return null;
            var z = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(z);
            foreach (var c in rect.Cells) if (c.GetZone(map) == null) z.AddCell(c);
            z.label = label;
            return z;
        }
    }

    // ============================================================================================================ M8 colony settings
    /// <summary>smd.colony (M8-1, M8-3): saved colony settings are applied at a new game's start, once; starting colonists on the old
    /// medical default move to the new one; a second start of the hook changes nothing.</summary>
    sealed class ColonySuite : KitTestSuite
    {
        public override string Name => "smd.colony";
        public override string Section => SmdSections.Colony;
        public override int MaxTicks => 1000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var ps = Find.PlaySettings;
            var before = (ps.defaultCareForColonist, ps.autoRebuild, ps.useWorkPriorities);
            var comp = SmdRoster.Current;
            var st = new T.Settings(t.Mod).Section(SmdSections.Colony, true).Set("smd.colonyApplyOnNewGame", true);
            try
            {
                t.Check(SmdGameData.colonyDefaultsApplied, "the new game ran the colony step once at its start");
                ps.defaultCareForColonist = MedicalCareCategory.HerbalOrWorse; ps.autoRebuild = true; ps.useWorkPriorities = true;
                t.Check(ColonySettingsStore.SaveCurrentGame(), "colony settings saved");
                ps.defaultCareForColonist = MedicalCareCategory.Best; ps.autoRebuild = false; ps.useWorkPriorities = false;
                var pawns = t.Map.mapPawns.FreeColonists.ToList();
                foreach (var p in pawns) p.playerSettings.medCare = MedicalCareCategory.Best;
                ColonyDefaults.ApplyAtNewGame(comp);
                t.Check(ps.defaultCareForColonist == MedicalCareCategory.HerbalOrWorse && ps.autoRebuild && ps.useWorkPriorities, "play settings set from the saved colony settings");
                t.Check(pawns.All(p => p.playerSettings.medCare == MedicalCareCategory.HerbalOrWorse), "colonists on the old default moved to the new medical default");
                t.Check(SmdLibraryApi.Status().hasColony, "the library reports saved colony settings");

                // the hook runs once per game: starting it again changes nothing
                ps.defaultCareForColonist = MedicalCareCategory.Best; ps.autoRebuild = false;
                SmdRoster.GameStarted();
                t.Check(ps.defaultCareForColonist == MedicalCareCategory.Best && !ps.autoRebuild, "a second new-game hook does not apply again (flag in the save)");
                yield return 1;
            }
            finally
            {
                ColonySettingsStore.Clear();
                ps.defaultCareForColonist = before.defaultCareForColonist; ps.autoRebuild = before.autoRebuild; ps.useWorkPriorities = before.useWorkPriorities;
                st.Restore();
            }
        }
    }

    // ============================================================================================================ M9 shelves
    /// <summary>smd.shelf (M9-1, M9-2): a shelf blueprint gets the default, a finished construction keeps it, an edited blueprint
    /// wins, and a shelf that is only (re)spawned (reinstall, gravship landing, load) keeps its own settings.</summary>
    sealed class ShelfSuite : KitTestSuite
    {
        public override string Name => "smd.shelf";
        public override string Section => SmdSections.Buildings;
        public override int MaxTicks => 1500;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var made = new List<Thing>();
            const string key = "Shelf", preset = "SMD test shelf";
            var st = new T.Settings(t.Mod).Set("smd.storageAutoApplyOnBuild", true);
            try
            {
                var model = T.Spawn(t, key); made.Add(model);
                var mf = ((IStoreSettingsParent)model).GetStoreSettings().filter;
                mf.SetDisallowAll(); mf.SetAllow(ThingDefOf.Steel, true);
                DefaultBillsController.SaveAsPreset(model, preset);
                t.Check(DefaultBillsDatabase.GetPresets(key)?.DefaultConfig != null, "shelf default saved");

                // reinstall / landing / load: a spawn outside construction never applies the default
                var other = T.Spawn(t, key); made.Add(other);
                bool allowsAll(Thing th) => ((IStoreSettingsParent)th).GetStoreSettings().filter.Allows(ThingDefOf.WoodLog);
                t.Check(allowsAll(other), "a shelf spawned outside construction keeps its own filter (reinstall, gravship, load)");

                // blueprint gets the default
                if (!TestCells.TryFindFreeRect(map, 4, 3, out var rect)) { t.Check(false, "room for a blueprint"); yield break; }
                var bp = GenConstruct.PlaceBlueprintForBuild(T.Def(key), rect.CenterCell, map, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
                made.Add(bp);
                var bpf = (bp as IStoreSettingsParent)?.GetStoreSettings()?.filter;
                t.Check(bpf != null && bpf.Allows(ThingDefOf.Steel) && !bpf.Allows(ThingDefOf.WoodLog), "the blueprint carries the default: " + T.Allowed(bpf, "Steel", "WoodLog"));

                // finish the construction through the vanilla frame path
                var worker = map.mapPawns.FreeColonists.FirstOrDefault();
                var pos = bp.Position;
                t.Check(((Blueprint)bp).TryReplaceWithSolidThing(worker, out Thing frameThing, out _) && frameThing is Frame, "blueprint became a frame (vanilla path)");
                var frame = frameThing as Frame;
                if (frame == null) yield break;
                made.Add(frame);
                frame.CompleteConstruction(worker);
                yield return 1;
                var built = pos.GetThingList(map).FirstOrDefault(x => x.def.defName == key);
                if (built != null) made.Add(built);
                t.Check(built != null && !allowsAll(built) && ((IStoreSettingsParent)built).GetStoreSettings().filter.Allows(ThingDefOf.Steel), "the finished shelf has the default");

                // an edited blueprint wins over the default
                // (a few tries: a pawn or an item walking into the cell blocks vanilla's blueprint → frame step; not this mod's logic)
                Frame frame2 = null; IntVec3 pos2 = IntVec3.Invalid;
                for (int attempt = 0; attempt < 4 && frame2 == null; attempt++)
                {
                    if (!TestCells.TryFindFreeRect(map, 4, 3, out var rect2, 3 + attempt * 4)) break;
                    var bp2 = GenConstruct.PlaceBlueprintForBuild(T.Def(key), rect2.CenterCell, map, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
                    made.Add(bp2);
                    var f2 = ((IStoreSettingsParent)bp2).GetStoreSettings().filter;
                    f2.SetAllow(ThingDefOf.WoodLog, true);
                    pos2 = bp2.Position;
                    ((Blueprint)bp2).TryReplaceWithSolidThing(worker, out Thing frameThing2, out _);
                    frame2 = frameThing2 as Frame;
                    if (frame2 == null) t.Note("blueprint → frame refused at " + pos2 + " (cell blocked), trying another place");
                }
                if (pos2.IsValid)
                {
                    if (frame2 == null) { t.Check(false, "second blueprint became a frame"); yield break; }
                    made.Add(frame2);
                    frame2.CompleteConstruction(worker);
                    yield return 1;
                    var built2 = pos2.GetThingList(map).FirstOrDefault(x => x.def.defName == key);
                    if (built2 != null) made.Add(built2);
                    t.Check(built2 != null && allowsAll(built2), "an edited blueprint keeps the player's edit after construction");
                }
            }
            finally
            {
                DefaultBillsDatabase.ClearAll(key);
                T.Kill(made.ToArray());
                st.Restore();
            }
        }
    }

    // ============================================================================================================ M9/M10 zones
    /// <summary>smd.zones (M9-3, M10-1): a new stockpile, dumping zone and growing zone drawn by the player get their defaults;
    /// a zone created in code (quest, ritual) is untouched.</summary>
    sealed class ZonesSuite : KitTestSuite
    {
        public override string Name => "smd.zones";
        public override string Section => SmdSections.Zones;
        public override int MaxTicks => 1000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var made = new List<Zone>();
            const string preset = "SMD test zone";
            var st = new T.Settings(t.Mod).Set("smd.zoneAutoApplyOnCreate", true).Set("smd.growingAutoApplyOnCreate", true);
            var make = AccessTools.Method(typeof(Designator_ZoneAdd), "MakeNewZone");
            try
            {
                // save defaults from configured zones
                var sp = BillTemplateSuite.MakeStockpile(map, "SMD model"); made.Add(sp);
                sp.settings.filter.SetDisallowAll(); sp.settings.filter.SetAllow(ThingDefOf.Steel, true);
                DefaultBillsController.SaveZonePreset(sp, DefaultBillsDatabase.ZoneStockpile, preset);
                var dumpModel = BillTemplateSuite.MakeStockpile(map, "SMD dump model"); made.Add(dumpModel);
                dumpModel.settings.filter.SetDisallowAll(); dumpModel.settings.filter.SetAllow(ThingDefOf.WoodLog, true);
                DefaultBillsController.SaveZonePreset(dumpModel, DefaultBillsDatabase.ZoneDumping, preset);
                var gz = new Zone_Growing(map.zoneManager); map.zoneManager.RegisterZone(gz); made.Add(gz);
                if (T.FreeZoneRect(map, 2, 2, true, out var r0)) foreach (var c in r0.Cells) if (c.GetZone(map) == null) gz.AddCell(c);
                gz.SetPlantDefToGrow(T.Def("Plant_Rice")); gz.allowSow = false;
                DefaultBillsController.SaveZonePreset(gz, DefaultBillsDatabase.ZoneGrowing, preset);

                // zones the player draws (the designators' MakeNewZone)
                var z1 = make.Invoke(new Designator_ZoneAddStockpile_Resources(), null) as Zone_Stockpile; made.Add(z1);
                t.Check(z1 != null && z1.settings.filter.Allows(ThingDefOf.Steel) && !z1.settings.filter.Allows(ThingDefOf.WoodLog), "new stockpile zone: default filter (" + T.Allowed(z1?.settings.filter, "Steel", "WoodLog") + ")");
                var z2 = make.Invoke(new Designator_ZoneAddStockpile_Dumping(), null) as Zone_Stockpile; made.Add(z2);
                t.Check(z2 != null && z2.settings.filter.Allows(ThingDefOf.WoodLog) && !z2.settings.filter.Allows(ThingDefOf.Steel), "new dumping zone: its own default (" + T.Allowed(z2?.settings.filter, "Steel", "WoodLog") + ")");
                var z3 = make.Invoke(new Designator_ZoneAdd_Growing(), null) as Zone_Growing; made.Add(z3);
                t.Check(z3 != null && z3.GetPlantDefToGrow()?.defName == "Plant_Rice" && !z3.allowSow, "new growing zone: rice, sow off (" + z3?.GetPlantDefToGrow()?.defName + ", sow " + z3?.allowSow + ")");

                // created in code: untouched
                var quest = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager); made.Add(quest);
                t.Check(quest.settings.filter.Allows(ThingDefOf.WoodLog), "a zone made in code (quest/ritual) keeps vanilla settings");

                // area off: nothing applied
                st.Section(SmdSections.Zones, false);
                var z4 = make.Invoke(new Designator_ZoneAddStockpile_Resources(), null) as Zone_Stockpile; made.Add(z4);
                t.Check(z4 != null && z4.settings.filter.Allows(ThingDefOf.WoodLog), "area switched off: a new stockpile stays vanilla");
                yield return 1;
            }
            finally
            {
                DefaultBillsDatabase.ClearAll(DefaultBillsDatabase.ZoneStockpile);
                DefaultBillsDatabase.ClearAll(DefaultBillsDatabase.ZoneDumping);
                DefaultBillsDatabase.ClearAll(DefaultBillsDatabase.ZoneGrowing);
                foreach (var z in made) if (z != null && z.zoneManager != null && map.zoneManager.AllZones.Contains(z)) z.Delete();
                st.Restore();
            }
        }
    }

    // ============================================================================================================ M9 intent
    /// <summary>smd.intent (M9-4): a filter saved as "all Foods except Pemmican" allows an item that was not in the stored list
    /// (as if a mod added it later), and keeps the exception. Capture reproduces the filter exactly for existing items.</summary>
    sealed class IntentSuite : KitTestSuite
    {
        public override string Name => "smd.intent";
        public override int MaxTicks => 200;

        public override IEnumerable<int> Run(TestContext t)
        {
            var foods = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Foods");
            var pemmican = T.Def("Pemmican"); var meal = T.Def("MealSimple");
            t.Check(foods != null && pemmican != null && meal != null, "Foods, Pemmican and MealSimple exist");
            if (foods == null || pemmican == null || meal == null) yield break;
            var parent = new ThingFilter(); parent.SetAllowAll(null);

            var f = new ThingFilter(); f.SetAllow(foods, true); f.SetAllow(pemmican, false);
            var intent = FilterIntent.Capture(f, parent);
            t.Note("captured ops: " + string.Join(" ", intent.ops.Take(12)) + (intent.ops.Count > 12 ? " …" : ""));
            t.Check(intent.ops.Contains("+c:Foods") && intent.ops.Contains("-d:Pemmican"), "intent = +c:Foods with the exception -d:Pemmican");
            var exact = new ThingFilter();
            intent.ApplyDefsTo(exact, parent);
            int diff = DefDatabase<ThingDef>.AllDefs.Count(d => parent.Allows(d) && exact.Allows(d) != f.Allows(d));
            t.Check(diff == 0, "capture is exact for every existing item (" + diff + " differ)");

            // the stored whitelist does not know MealSimple (it "did not exist" when the preset was saved)
            var stored = new ThingFilter(); stored.CopyAllowancesFrom(f); stored.SetAllow(meal, false);
            var target = new ThingFilter();
            FilterIntent.ApplyStored(target, stored, intent, parent);
            t.Check(target.Allows(meal) && !target.Allows(pemmican), "an item added later under Foods is allowed, Pemmican stays out");
            var st = new T.Settings(t.Mod).Set("smd.filterIntent", false);
            var plain = new ThingFilter();
            FilterIntent.ApplyStored(plain, stored, intent, parent);
            st.Restore();
            t.Check(!plain.Allows(meal), "with the setting off the stored list is used as it is (1.x behaviour)");
        }
    }

    // ============================================================================================================ bench families
    /// <summary>smd.families (F-1, M2-1 mixed): a preset only on the electric stove is used by a new fueled stove; the fueled stove's
    /// own preset wins; families off = nothing.</summary>
    sealed class FamiliesSuite : KitTestSuite
    {
        public override string Name => "smd.families";
        public override string Section => SmdSections.Buildings;
        public override int MaxTicks => 1000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var made = new List<Thing>();
            var st = new T.Settings(t.Mod).Set("smd.benchFamilies", true).Set("smd.autoApplyOnPlace", true);
            try
            {
                var e = T.Def("ElectricStove"); var fu = T.Def("FueledStove");
                t.Check(BenchFamilies.Score(e, fu) >= BenchFamilies.Threshold, "electric and fueled stove are related (" + BenchFamilies.Score(e, fu) + ")");
                var model = T.Spawn(t, "ElectricStove"); made.Add(model);
                T.AddBill(model, "CookMealSimple"); T.AddBill(model, "CookMealSimple"); T.AddBill(model, "CookMealFine");
                DefaultBillsController.SaveAsPreset(model, "SMD test stove");
                var f1 = T.Spawn(t, "FueledStove"); made.Add(f1);
                yield return 1;
                t.Check(T.Bills(f1) == 3, "a new fueled stove got the electric stove's 3 bills (" + T.Bills(f1) + ")");
                // own preset wins
                var own = T.Spawn(t, "FueledStove"); made.Add(own);
                (own as IBillGiver).BillStack.Clear();
                T.AddBill(own, "CookMealSimple");
                DefaultBillsController.SaveAsPreset(own, "SMD test fueled");
                var f2 = T.Spawn(t, "FueledStove"); made.Add(f2);
                yield return 1;
                t.Check(T.Bills(f2) == 1, "with its own preset the fueled stove uses that one (" + T.Bills(f2) + ")");
                DefaultBillsDatabase.ClearAll("FueledStove");
                st.Set("smd.benchFamilies", false);
                var f3 = T.Spawn(t, "FueledStove"); made.Add(f3);
                yield return 1;
                t.Check(T.Bills(f3) == 0, "families off: nothing (" + T.Bills(f3) + ")");
                // mixed selection with families on: both kinds get the bills
                st.Set("smd.benchFamilies", true);
                (f3 as IBillGiver).BillStack.Clear(); (model as IBillGiver).BillStack.Clear();
                Find.Selector.ClearSelection(); Find.Selector.Select(model, false, false); Find.Selector.Select(f3, false, false);
                DefaultBillsController.ApplyPreset(model, "ElectricStove", "SMD test stove");
                t.Check(T.Bills(model) == 3 && T.Bills(f3) == 3, "mixed selection (electric + fueled): both got the bills");
            }
            finally
            {
                Find.Selector.ClearSelection();
                DefaultBillsDatabase.ClearAll("ElectricStove"); DefaultBillsDatabase.ClearAll("FueledStove");
                T.Kill(made.ToArray());
                st.Restore();
            }
        }
    }

    // ============================================================================================================ migration of 1.x preset files
    /// <summary>smd.migration (M4-1 with fixtures, M4-3): the 1.x preset fixtures (incl. a broken file) import with the same names
    /// and defaults, every migrated config equals its source, the files are not changed, a re-import adds nothing. Needs
    /// SMD_TEST_FIXTURES (the repo's Source/Tests/fixtures/legacy_v13); explicit only.</summary>
    sealed class MigrationSuite : KitTestSuite
    {
        public override string Name => "smd.migration";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 300;

        public override IEnumerable<int> Run(TestContext t)
        {
            string dir = Environment.GetEnvironmentVariable("SMD_TEST_FIXTURES");
            t.Check(!string.IsNullOrEmpty(dir) && Directory.Exists(dir), "fixture folder given (SMD_TEST_FIXTURES)");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
            var files = Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories);
            var hashes = files.ToDictionary(f => f, LegacyMigration.Sha256);
            var lib = new SmdLibrary();
            var r = LegacyMigration.ImportAll(dir, lib, false);
            t.Note("result: " + r);
            t.Check(r.buildingPresets >= 5 && r.pawnPresets >= 2 && r.policies, "building, colonist and policy presets imported (" + r + ")");
            t.Check(r.errors.Count == 1 && r.errors[0].Contains("Broken"), "the broken file is reported, the others still imported (" + string.Join("; ", r.errors) + ")");
            t.Check(files.All(f => LegacyMigration.Sha256(f) == hashes[f]), "no legacy file was changed (" + files.Length + " SHA-256 equal)");
            // each migrated workbench config equals the file's own config element (verbatim copy)
            int same = 0, total = 0;
            foreach (var f in Directory.GetFiles(Path.Combine(dir, LegacyMigration.WorkbenchFolder), "*.xml"))
            {
                XDocument doc; try { doc = XDocument.Load(f); } catch { continue; }
                foreach (var p in doc.Descendants("li").Where(e => e.Element("name") != null && e.Element("config") != null))
                {
                    total++;
                    var key = doc.Root.Element("workbench")?.Value ?? Path.GetFileNameWithoutExtension(f);
                    var m = lib.content.Buildings(key)?.Get(p.Element("name").Value);
                    if (m != null && XNode.DeepEquals(new XElement(p.Element("config")) { Name = "config" }, m.config)) same++;
                }
            }
            t.Check(total == 0 || same == total, "migrated configs equal their source (" + same + "/" + total + ")");
            // the live templates read without red errors (counted while reading, not just left to the run report)
            int read = 0, readOk = 0, mark = t.Mark();
            foreach (var set in lib.content.buildings)
                foreach (var p in set.presets)
                {
                    var snap = new ConfigSnapshot();
                    read++;
                    if (SmdScribe.Read(p.config, snap.ExposeData)) readOk++;
                }
            t.Check(read > 0 && readOk == read, "every migrated preset reads back through the 1.x reader (" + readOk + "/" + read + ")");
            t.CheckNoRedErrors(mark, "reading every migrated preset");
            var r2 = LegacyMigration.ImportAll(dir, lib, false);
            t.Check(r2.buildingPresets == 0 && r2.pawnPresets == 0 && r2.skippedExisting > 0, "a second import adds nothing (" + r2 + ")");
            yield return 1;
        }
    }

    /// <summary>smd.legacyreal (explicit): the slot started with a read-only copy of the player's real 1.x preset folders
    /// (SMD_LEGACY_DIR). The library was created from it on first access with every preset and default; the files are unchanged.</summary>
    sealed class LegacyRealSuite : KitTestSuite
    {
        public override string Name => "smd.legacyreal";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 300;

        public override IEnumerable<int> Run(TestContext t)
        {
            string dir = Environment.GetEnvironmentVariable("SMD_LEGACY_DIR");
            t.Check(!string.IsNullOrEmpty(dir) && Directory.Exists(dir), "legacy folder given (SMD_LEGACY_DIR)");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
            var files = Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories);
            var hashes = files.ToDictionary(f => f, LegacyMigration.Sha256);
            var status = SmdLibraryApi.Status();
            t.Note("library: " + status.buildingSets + " building types, " + status.buildingPresets + " presets, " + status.pawnPresets + " colonist presets; migration: " + (status.lastMigration ?? "none this session"));
            t.Check(!status.readOnly, "library writable");
            t.Check(File.Exists(SmdPaths.LibraryFile), "Library.xml created");
            // what the files hold: one set per workbench file (v3: named presets; v1/v2: one "Default")
            var check = new SmdLibrary();
            var expect = LegacyMigration.ImportAll(dir, check, false);
            t.Check(status.buildingSets == expect.buildingSets && status.buildingPresets == expect.buildingPresets && status.pawnPresets == expect.pawnPresets,
                "every preset arrived (" + status.buildingPresets + "/" + expect.buildingPresets + " building, " + status.pawnPresets + "/" + expect.pawnPresets + " colonist)");
            t.Check(expect.errors.Count == 0, "no file of the player was unreadable (" + string.Join("; ", expect.errors) + ")");
            int sameDefault = 0, sets = 0;
            foreach (var s in check.content.buildings)
            {
                sets++;
                var live = SmdLibraryStore.Content.Buildings(s.key);
                if (live != null && live.defaultName == s.defaultName && live.presets.Select(p => p.name).SequenceEqual(s.presets.Select(p => p.name))) sameDefault++;
            }
            t.Check(sameDefault == sets, "same names and defaults per building (" + sameDefault + "/" + sets + ")");
            // the live templates read back (unknown defs of mods not in this run are skipped silently)
            int read = 0, unread = 0, missingMods = 0;
            foreach (var s in SmdLibraryStore.Content.buildings)
                foreach (var p in s.presets)
                {
                    var snap = new ConfigSnapshot();
                    if (SmdScribe.Read(p.config, snap.ExposeData)) read++; else unread++;
                    if (!SmdDefRefs.Missing(p).IsEmpty) missingMods++;
                }
            t.Check(unread == 0, "every preset reads into a live template (" + read + " read, " + unread + " failed; " + missingMods + " need mods not in this run)");
            t.Check(files.All(f => LegacyMigration.Sha256(f) == hashes[f]), "the player's files are unchanged (" + files.Length + " SHA-256 equal)");
            yield return 1;
        }
    }

    // ============================================================================================================ Nice Bill Tab
    /// <summary>smd.nicebilltab (NBT-1, explicit: needs Nice Bill Tab or Nice Inventory Tab in the run): applying a preset raises
    /// Nice Bill Tab's refresh flag, so the bills show up at once in the open tab.</summary>
    sealed class NiceBillTabSuite : KitTestSuite
    {
        public override string Name => "smd.nicebilltab";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 600;

        public override IEnumerable<int> Run(TestContext t)
        {
            foreach (var id in KitNiceBillTab.PackageIds)
                t.Note(id + ": ModsConfig.IsActive " + ModsConfig.IsActive(id) + ", ModLister " + (ModLister.GetActiveModWithIdentifier(id, true) != null) + ", KitDetect " + KitDetect.IsActive(id));
            t.Check(KitNiceBillTab.Active, "Nice Bill Tab code is loaded (framework adapter; active ids: " + string.Join(", ", KitNiceBillTab.PackageIds.Where(KitDetect.IsActive)) + ")");
            t.Check(t.Mod.CompatOn(SmdCompat.NiceBillTabId), "the integration is on (Compatibility page switch, on while Nice Bill Tab is present)");
            var flag = AccessTools.Field(AccessTools.TypeByName("NiceBillTab.TabBillsDrawer"), "shouldRefreshFilter");
            t.Check(flag != null, "its refresh flag exists");
            if (flag == null) yield break;
            Building model = null, bench = null;
            try
            {
                model = T.Spawn(t, "TableButcher");
                T.AddBill(model, "ButcherCorpseFlesh"); T.AddBill(model, "ButcherCorpseFlesh");
                DefaultBillsController.SaveAsPreset(model, "SMD test nbt");
                bench = T.Spawn(t, "TableButcher");
                (bench as IBillGiver).BillStack.Clear();
                Find.Selector.ClearSelection(); Find.Selector.Select(bench, false, false);
                var tab = bench.GetInspectTabs()?.OfType<ITab_Bills>().FirstOrDefault();
                if (tab != null) { InspectPaneUtility.OpenTab(typeof(ITab_Bills)); }
                t.Note("Bills tab " + (tab != null ? "opened" : "not found") + " for the bench");
                yield return 5;
                flag.SetValue(null, false);
                DefaultBillsController.ApplyPreset(bench, "TableButcher", "SMD test nbt");
                bool raised = (bool)flag.GetValue(null);
                t.Check(T.Bills(bench) == 2, "the preset was applied (2 bills)");
                t.Check(raised, "Nice Bill Tab was told to refresh right after applying");
                yield return 120;
                // Nice Bill Tab clears the flag when it draws its tab; in an offscreen test run the inspect pane may not be drawn.
                t.Note("after 120 ticks the flag is " + ((bool)flag.GetValue(null) ? "still set (tab not drawn in this run)" : "cleared: Nice Bill Tab rebuilt its list"));
                // the harness flips the switch the Compatibility page shows (TestContext.SetCompat, framework pass 3)
                t.Check(t.SetCompat(SmdCompat.NiceBillTabId, false) && !t.Mod.CompatOn(SmdCompat.NiceBillTabId), "integration switched off");
                flag.SetValue(null, false);
                DefaultBillsController.ApplyPreset(bench, "TableButcher", "SMD test nbt");
                t.Check(!(bool)flag.GetValue(null), "integration switched off on the Compatibility page: no refresh request");
                t.SetCompat(SmdCompat.NiceBillTabId, true);
            }
            finally
            {
                Find.Selector.ClearSelection();
                DefaultBillsDatabase.ClearAll("TableButcher");
                T.Kill(model, bench);
            }
        }
    }

    // ============================================================================================================ switch-off rule
    /// <summary>smd.sections (S-1, G13a): every area switched off hides its buttons and stops its auto-apply; presets stay.</summary>
    sealed class SectionsSuite : KitTestSuite
    {
        public override string Name => "smd.sections";
        public override int MaxTicks => 1000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var made = new List<Thing>();
            var st = new T.Settings(t.Mod);
            try
            {
                foreach (var id in SmdSections.All) t.Check(t.Mod.On(id), "area " + id + " is on by default");
                var model = T.Spawn(t, "TableButcher"); made.Add(model);
                T.AddBill(model, "ButcherCorpseFlesh");
                DefaultBillsController.SaveAsPreset(model, "SMD test sections");
                bool HasGizmo(Thing th) => th.GetGizmos().OfType<Command>().Any(c => c.defaultLabel == "WBD_Defaults".Translate().ToString());
                t.Check(HasGizmo(model), "area on: the Defaults button is on the bench");
                st.Section(SmdSections.Buildings, false);
                t.Check(!HasGizmo(model), "area off: no Defaults button");
                var b = T.Spawn(t, "TableButcher"); made.Add(b);
                yield return 1;
                t.Check(T.Bills(b) == 0, "area off: a new bench gets no default bills");
                t.Check(DefaultBillsDatabase.HasPresets("TableButcher"), "area off: the preset is still saved");
                st.Restore();
                var pawn = t.Map.mapPawns.FreeColonists.FirstOrDefault();
                bool PawnGizmo() => pawn.GetGizmos().OfType<Command>().Any(c => c.defaultLabel == "WBD_PawnDefaults".Translate().ToString());
                t.Check(PawnGizmo(), "colonists on: Defaults button on a colonist");
                st.Section(SmdSections.Pawns, false);
                t.Check(!PawnGizmo(), "colonists off: no Defaults button on colonists");
                st.Restore();
                t.Check(t.Mod.Current.SectionOn(SmdSections.Pawns) && PawnGizmo(), "restored: the Colonists area is on again and its button is back");
            }
            finally
            {
                DefaultBillsDatabase.ClearAll("TableButcher");
                T.Kill(made.ToArray());
                st.Restore();
            }
        }
    }

    // ============================================================================================================ save data
    /// <summary>smd.savedata (R-1, B8): the roster lives in the framework's save node, our old components are never written, and a
    /// save → load round trip keeps known, baseline and the colony flag.</summary>
    sealed class SaveDataSuite : KitTestSuite
    {
        public override string Name => "smd.savedata";
        public override int MaxTicks => 1500;

        public override IEnumerable<int> Run(TestContext t)
        {
            int known = SmdGameData.KnownCount, version = SmdGameData.rosterVersion, baseline = SmdGameData.baselineTick;
            bool colony = SmdGameData.colonyDefaultsApplied;
            // a queued blueprint config (Blueprints Forked) lives in the framework's per-map data
            var model = T.Spawn(t, "TableButcher");
            T.AddBill(model, "ButcherCorpseFlesh");
            var snap = ConfigSnapshot.Capture(model);
            T.Kill(model);
            var bdef = T.Def("TableButcher");
            IntVec3 cell = IntVec3.Invalid;
            if (TestCells.TryFindFreeRect(t.Map, 5, 4, out var rect))
            {
                cell = rect.CenterCell;
                GenConstruct.PlaceBlueprintForBuild(bdef, cell, t.Map, Rot4.North, Faction.OfPlayer, GenStuff.DefaultStuffFor(bdef));
                SmdPending.Register(t.Map, cell, bdef, snap);
            }
            t.Check(cell.IsValid && SmdMapData.Peek(t.Map)?.Count == 1, "a blueprint config is queued on the map");
            string name = "jb_smd_savedata";
            GameDataSaveLoader.SaveGame(name);
            yield return 5;
            string path = GenFilePaths.FilePathForSavedGame(name);
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            t.Check(text.Length > 0, "save written");
            t.Check(!text.Contains("BetterWorkbenchDefaults.PawnDefaultsGameComponent") && !text.Contains("BetterWorkbenchDefaults.WBD_PendingConfigs"), "no class of this mod in the save (removal-safe)");
            t.Check(!text.Contains("Class=\"BetterWorkbenchDefaults."), "no Class attribute naming this mod anywhere in the save");
            t.Check(text.Contains("<rosterVersion>1</rosterVersion>") && text.Contains("<known>"), "the roster is saved inside the framework's node");
            t.Check(!Current.Game.components.Any(c => c is PawnDefaultsGameComponent) && !t.Map.components.Any(c => c is WBD_PendingConfigs), "no component of this mod in the running game (the old ones are legacy only)");
            // read the node back the way a load does
            SmdGameData.Reset();
            t.Check(SmdGameData.KnownCount == 0, "reset clears the data (as before a load)");
            var doc = XDocument.Parse(text);
            var node = doc.Descendants("li").FirstOrDefault(e => (string)e.Element("id") == "juliusberger.workbenchbilldefaults")?.Element("data");
            t.Check(node != null, "the mod's data node found in the save");
            if (node != null)
            {
                string tmp = Path.Combine(GenFilePaths.SaveDataFolderPath, "smd_node.xml");
                File.WriteAllText(tmp, "<root>" + string.Concat(node.Nodes()) + "</root>");
                Scribe.loader.InitLoading(tmp);
                try { SmdGameData.Expose(); Scribe.loader.FinalizeLoading(); }
                catch { Scribe.ForceStop(); throw; }
                File.Delete(tmp);
            }
            t.Check(SmdGameData.KnownCount == known && SmdGameData.rosterVersion == version && SmdGameData.baselineTick == baseline && SmdGameData.colonyDefaultsApplied == colony,
                "known " + SmdGameData.KnownCount + "/" + known + ", version, baseline and colony flag came back");
            var mapNode = doc.Descendants("li").FirstOrDefault(e => (string)e.Element("id") == "juliusberger.workbenchbilldefaults" && e.Parent?.Parent?.Attribute("Class")?.Value == typeof(FrameworkMap).FullName);
            t.Check(mapNode?.Element("data")?.Element("pending")?.Elements("li").Count() == 1, "the queued config is in the framework's map node of the save");

            // an early 2.0 save kept the pending configs in the game node (keyed by map): read once, moved to the map's data
            if (cell.IsValid && node != null)
            {
                SmdMapData.Reset(t.Map);
                string ctmp = Path.Combine(GenFilePaths.SaveDataFolderPath, "smd_cfg.xml");
                Scribe.saver.InitSaving(ctmp, "root");
                try { Scribe_Deep.Look(ref snap, "config"); } finally { Scribe.saver.FinalizeSaving(); }
                var cfg = XDocument.Load(ctmp).Root.Element("config");
                var legacy = new XElement("root", new XElement("rosterVersion", 1), new XElement("pending", new XElement("li", new XElement("map", t.Map.uniqueID),
                    new XElement("entries", new XElement("li", new XElement("cell", cell.ToString()), new XElement("def", "TableButcher"), cfg)))));
                File.WriteAllText(ctmp, legacy.ToString());
                SmdGameData.Reset();
                Scribe.loader.InitLoading(ctmp);
                try { SmdGameData.Expose(); Scribe.loader.FinalizeLoading(); }
                catch { Scribe.ForceStop(); throw; }
                File.Delete(ctmp);
                SmdGameData.MoveLegacyPending();
                var moved = SmdMapData.Peek(t.Map);
                t.Check(moved != null && moved.Count == 1 && moved[0].cell == cell && moved[0].def == bdef && !moved[0].snapshot.IsEmpty,
                    "a pending config from the game node of an early 2.0 save moved to its map (" + (moved?.Count ?? 0) + ")");
                foreach (var th in cell.GetThingList(t.Map).Where(th => th is Blueprint).ToList()) th.Destroy();
                SmdMapData.Reset(t.Map);
                // put this game's own data back (the legacy read replaced it)
                SmdGameData.Reset();
                string tmp2 = Path.Combine(GenFilePaths.SaveDataFolderPath, "smd_node.xml");
                File.WriteAllText(tmp2, "<root>" + string.Concat(node.Nodes()) + "</root>");
                Scribe.loader.InitLoading(tmp2);
                try { SmdGameData.Expose(); Scribe.loader.FinalizeLoading(); }
                catch { Scribe.ForceStop(); throw; }
                File.Delete(tmp2);
                t.Check(SmdGameData.KnownCount == known && SmdGameData.baselineTick == baseline, "this game's roster is back after the legacy test");
            }
            try { File.Delete(path); } catch { }
        }
    }
}
#endif
