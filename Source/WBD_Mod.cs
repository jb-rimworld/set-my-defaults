using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JBFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Mod entry point on JB Framework (2.0). The class name stays WBD_Mod: it names the settings file
    /// (Mod_BetterWorkbenchDefaults_WBD_Mod.xml), so no player loses values. The framework applies every [HarmonyPatch] class of this
    /// assembly one by one; the two manual patches (building spawn hook, bill-cap transpiler) are applied here.
    ///
    /// Per save: the framework's template (settings file) holds the defaults for new colonies; each save keeps its own copy ("this
    /// save" = what this colony uses). Interface preferences, the bill limit and the new-game switches are the same in every game
    /// (AsGlobal). The presets themselves (bills, shelves, zones, colonists, policies, colony settings) always live in the cross-save
    /// library, never in a save: that is the point of the mod.
    /// </summary>
    public sealed class WBD_Mod : KitModBase
    {
        public const string PackageId = "juliusberger.workbenchbilldefaults";

        /// <summary>The settings object (properties that read and write the framework's values).</summary>
        public static WBD_Settings Settings => K?.Settings as WBD_Settings;

        /// <summary>This mod's framework context (null only before RimWorld created the mod class).</summary>
        internal static KitMod K => Framework.Of<WBD_Mod>();

        /// <summary>True once the live bill-cap transpiler is in place (false when No Max Bills owns the limit or the patch failed).</summary>
        public static bool BillCapPatched { get; private set; }

        public WBD_Mod(ModContentPack content) : base(content, new KitInfo
        {
            Name = "Set my Defaults",
            ShortId = "SMD",
            KeyPrefix = "SMD",
            Version = "2.0.0",
            PerSave = true,
            WorkshopId = "3737034968",
            Url = "https://github.com/jb-rimworld/set-my-defaults",
            LicenseLine = "MIT",
            HarmonyId = PackageId,
            // No FirstStartPitch: this mod draws its own welcome card (profiles), see Dialog_SmdStartCard. FW-REQUEST (7).
            Theme = new KitTheme { Accent = new Color(0.47f, 0.72f, 0.45f), MarkTex = "UI/WBD_Defaults" },
        })
        {
            var harmony = new Harmony(PackageId + ".manual");
            try { Patch_SpawnSetup.Apply(harmony); }
            catch (Exception e) { WBD_Log.Warn("The building spawn hook could not be applied; new buildings will not get their defaults: " + e.Message); }
            // Native >15 bills: the transpiler reads BillCap.Current() live (no restart). Not registered when a "No Max Bills" mod
            // already owns the limit (no double patch).
            if (!BillCapCompat.Active)
                TryPatchBillCap(harmony);
        }

        protected override void Register(KitRegistry r)
        {
            // ---------------------------------------------------------------- areas (G13a: off = hidden, no new effects; saved presets stay)
            r.Section(SmdSections.Buildings, "Buildings",
                    "Default bills for workbenches, default filters for shelves and other storage, default plants for hydroponics. The Defaults button on buildings and in the Bills tab.")
                .WithOrder(10).LegacyEnabled("sectionBuildings")
                .WithDiagnosis(() => new[] { "Building preset sets: " + SafeStatus(s => s.buildingSets) + ", presets: " + SafeStatus(s => s.buildingPresets) });
            r.Section(SmdSections.Zones, "Zones",
                    "Default filters for new stockpile and dumping zones, default plant for new growing zones. The Defaults button on zones.")
                .WithOrder(20).LegacyEnabled("sectionZones")
                .WithDiagnosis(() => new[] { "Zone presets: " + SafeStatus(s => s.zonePresets) });
            r.Section(SmdSections.Pawns, "Colonists",
                    "Colonist presets (schedule, work, policies, hostility, medical care) and applying the default preset once to every new colonist. The Defaults button on colonists.")
                .WithOrder(30).LegacyEnabled("sectionPawns")
                .WithDiagnosis(() => new[] { "Colonist presets: " + SafeStatus(s => s.pawnPresets) + "; known colonists in this save: " + SmdGameData.KnownCount });
            r.Section(SmdSections.Policies, "Policies",
                    "A saved set of outfit, drug, food and reading policies that you can apply in any colony, and optionally to every new game.")
                .WithOrder(40).LegacyEnabled("sectionPolicies")
                .WithDiagnosis(() => new[] { "Policy set saved: " + SafeStatus(s => s.hasPolicies) });
            r.Section(SmdSections.Colony, "Colony settings",
                    "Medical care defaults per group, auto home area, auto rebuild and manual work priorities, saved once and applied to new games.")
                .WithOrder(50).LegacyEnabled("sectionColony")
                .WithDiagnosis(() => new[] { "Colony settings saved: " + SafeStatus(s => s.hasColony) });
            r.Section(SmdSections.Library, "Library",
                    "The cross-save library file that holds every preset, named profiles and share strings for importing and exporting.")
                .WithOrder(60).LegacyEnabled("sectionLibrary")
                .WithDiagnosis(LibraryDiagnosis);

            // ---------------------------------------------------------------- Workbenches and shelves
            const string B = SmdSections.Buildings;
            r.Bool(B, "smd.autoApplyOnPlace", true, "Default bills on new workbenches",
                    "A newly built workbench gets the bills you saved as its default.").InGroup("Automatic").Legacy("autoApplyOnPlace");
            r.Bool(B, "smd.storageAutoApplyOnBuild", true, "Default filter on new shelves",
                    "A newly built shelf or other storage gets its default filter and priority; a moved or reinstalled one keeps its own.").InGroup("Automatic").Legacy("storageAutoApplyOnBuild");
            r.Bool(B, "smd.plantAutoApplyOnBuild", true, "Default plant on new hydroponics",
                    "A newly built hydroponics basin, plant pot or other plant grower gets its default plant.").InGroup("Automatic").Legacy("plantAutoApplyOnBuild");
            r.Bool(B, "smd.benchFamilies", true, "Related buildings share defaults",
                    "A building without its own preset uses the default of a close relative, e.g. the fueled stove uses the electric stove's bills.").InGroup("Automatic").Legacy("benchFamilies");
            r.Bool(B, "smd.billMatchStockpileByName", true, "Restore stockpiles by name",
                    "Bills that drop in or count in a specific stockpile find a stockpile of the same name again in the new colony.").InGroup("Bills").Legacy("billMatchStockpileByName");
            r.Bool(B, "smd.showGizmo", true, "Defaults button on buildings",
                    "Shows the Defaults button when a workbench, shelf or plant grower is selected.").InGroup("Buttons").AsGlobal().Legacy("showGizmo");
            r.Bool(B, "smd.showTabButton", true, "Defaults button in the Bills tab",
                    "Shows the Defaults button inside the Bills tab of a workbench (a small icon when Nice Bill Tab is used).").InGroup("Buttons").AsGlobal().Legacy("showTabButton");

            // ---------------------------------------------------------------- Stockpiles and growing zones
            const string Z = SmdSections.Zones;
            r.Bool(Z, "smd.zoneAutoApplyOnCreate", true, "Default filter on new stockpiles",
                    "A stockpile or dumping zone you draw gets the default you saved for that kind of zone.").InGroup("New zones").Legacy("zoneAutoApplyOnCreate");
            r.Bool(Z, "smd.growingAutoApplyOnCreate", true, "Default plant on new growing zones",
                    "A growing zone you draw gets the default plant and the sow and cut switches you saved.").InGroup("New zones").Legacy("growingAutoApplyOnCreate");

            // ---------------------------------------------------------------- Colonists
            const string P = SmdSections.Pawns;
            r.Bool(P, "smd.autoApplyToNewPawns", true, "Apply the default preset to new colonists",
                    "Every colonist who joins (start, recruit, wanderer, quest, birth) gets the default preset once. Colonists you already had are never changed.").InGroup("New colonists").Legacy("autoApplyToNewPawns");
            r.Bool(P, "smd.pawnIncludeChildren", true, "Include children",
                    "Children get the preset too; their work priorities are set once they are old enough to work.").InGroup("New colonists").Legacy("pawnIncludeChildren");
            r.Bool(P, "smd.pawnSkipWorkUnderFreeWill", true, "Leave work to Free Will",
                    "With Free Will or Free-ish Will, work priorities are skipped for colonists that mod manages; everything else still applies.").InGroup("New colonists").Legacy("pawnSkipWorkUnderFreeWill");
            r.Bool(P, "smd.pawnShowGizmo", true, "Defaults button on colonists",
                    "Shows the Defaults button when a colonist is selected.").InGroup("Buttons").AsGlobal().Legacy("pawnShowGizmo");
            const string Axes = "What a preset sets";
            r.Bool(P, "smd.pawnApplySchedule", true, "Schedule", "The 24-hour timetable.").InGroup(Axes).Legacy("pawnApplySchedule");
            r.Bool(P, "smd.pawnApplyWork", true, "Work priorities", "The work tab priorities (and manual priority numbers).").InGroup(Axes).Legacy("pawnApplyWork");
            r.Bool(P, "smd.pawnApplyApparel", true, "Outfit policy", "The apparel policy, matched by name or recreated.").InGroup(Axes).Legacy("pawnApplyApparel");
            r.Bool(P, "smd.pawnApplyDrug", true, "Drug policy", "The drug policy, matched by name or recreated.").InGroup(Axes).Legacy("pawnApplyDrug");
            r.Bool(P, "smd.pawnApplyFood", true, "Food policy", "The food restriction, matched by name or recreated.").InGroup(Axes).Legacy("pawnApplyFood");
            r.Bool(P, "smd.pawnApplyReading", true, "Reading policy", "The reading policy, matched by name or recreated.").InGroup(Axes).Legacy("pawnApplyReading");
            r.Bool(P, "smd.pawnApplyHostility", true, "Hostility response", "Flee, attack or ignore when enemies come close.").InGroup(Axes).Legacy("pawnApplyHostility");
            r.Bool(P, "smd.pawnApplyMedical", true, "Medical care", "The medicine the colonist may receive, and self-tend.").InGroup(Axes).Legacy("pawnApplyMedical");

            // ---------------------------------------------------------------- Policies and colony settings (new games: the same in every game)
            r.Bool(SmdSections.Policies, "smd.policyAutoApplyOnNewGame", false, "Apply the saved policies to new games",
                    "When a new colony starts, the saved policy set is created and starting colonists move to its defaults.").InGroup("New games").AsGlobal().Legacy("policyAutoApplyOnNewGame");
            r.Bool(SmdSections.Colony, "smd.colonyApplyOnNewGame", true, "Apply the saved colony settings to new games",
                    "When a new colony starts, the saved medical defaults, auto home area, auto rebuild and manual priorities are set (only if you saved any).").InGroup("New games").AsGlobal().Legacy("colonyApplyOnNewGame");

            // ---------------------------------------------------------------- General
            r.Bool(KitMod.Core, "smd.nativeRaiseBillCap", false, "Allow more than 15 bills",
                    "Lifts vanilla's limit of 15 bills per workbench without another mod. Not used while No Max Bills is installed.").InGroup("Bill limit").AsGlobal().Legacy("nativeRaiseBillCap");
            r.Int(KitMod.Core, "smd.maxBillsCap", 50, 15, 250, "Bills per workbench",
                    "The limit while more than 15 bills are allowed (also used when restoring defaults under No Max Bills). Many bills cost performance.")
                .InGroup("Bill limit").AsGlobal().HardLimits(15, 250).Legacy("maxBillsCap");
            r.Bool(KitMod.Core, "smd.filterIntent", true, "Remember categories in filters",
                    "Saved filters remember whole categories, so items a mod adds to a category later are included. Off = exact item lists as in 1.x.").InGroup("Filters").AsGlobal().AsNerd().Legacy("filterIntent");

            // ---------------------------------------------------------------- Compatibility switches (G13c: on when the mod is present)
            r.Bool(KitMod.Core, "smd.compatNiceBillTab", true, "Nice Bill Tab: refresh after applying",
                    "Tells Nice Bill Tab to redraw its list, so applied bills show up at once.").InGroup("Compatibility").AsGlobal();
            r.Bool(KitMod.Core, "smd.compatPrf", true, "Project RimFactory machines",
                    "S.A.L. assemblers get their bills after their recipes appear, storage machines and splitters get their defaults.").InGroup("Compatibility").AsGlobal();
            r.Bool(KitMod.Core, "smd.compatBlueprints", true, "Blueprints remember settings",
                    "With Blueprints Forked, a blueprint remembers each building's bills and storage settings and recreates them when built.").InGroup("Compatibility").AsGlobal();

            // ---------------------------------------------------------------- presets (first-start card and Overview)
            foreach (var m in Modes)
            {
                var mode = m;
                r.Preset(mode.Id, mode.LabelEn, mode.DescEn, mode.Apply).HighlightWhen(mode.Matches);
            }

            // ---------------------------------------------------------------- compatibility (G13c)
            r.Compat("Nice Bill Tab", "Andromeda.NiceBillTab", "The Defaults button sits as an icon in its bill window; applied bills show up at once.").HealthyWhen(() => SmdCompat.NiceBillTabLoaded);
            r.Compat("Nice Inventory Tab", "Andromeda.NiceInventoryTab", "Its bundled bill tab is supported the same way as Nice Bill Tab.").HealthyWhen(() => SmdCompat.NiceBillTabLoaded);
            r.Compat("No Max Bills Redux", "justharry.nomaxbillsredux", "Owns the bill limit; Set my Defaults restores at most that many bills and adds no patch of its own.");
            r.Compat("Project RimFactory Revived", "spdskatr.projectrimfactory", "Assemblers, storage machines and conveyor splitters get their defaults.").HealthyWhen(() => PrfCompat.PatchApplied);
            r.Compat("Blueprints Forked", "Defi.Blueprints.fork", "Blueprints remember bills and storage settings, even across saves.").HealthyWhen(() => BlueprintsCompat.Hooked);
            r.Compat("Free Will", "freemapa.freewill", "Work priorities are left to Free Will for the colonists it manages.");
            r.Compat("Free-ish Will", "stoneman.freewill", "Work priorities are left to Free-ish Will for the colonists it manages.");
            r.Compat("Better Workbench Management", "falconne.BWM", "Works side by side. A default bill uses the store mode saved in the preset, not BWM's own default.");
            r.Compat("Combat Extended", "CETeam.CombatExtended", "Fine: only vanilla schedule, work, policy and medical state is touched, never loadouts or ammo.");
            r.Compat("Ideology", KitDetect.IdeologyId, "A bill's style is saved with it and restored when that style exists.");
            r.Compat("Anomaly", KitDetect.AnomalyId, "Medical defaults for entities and ghouls are part of the colony settings.");

            // ---------------------------------------------------------------- custom pages (presets and the library are not plain numbers)
            // "Saved defaults" lists everything stored (all areas); profiles and import/export belong to the Library area. The same
            // pages open in game from the Defaults buttons (Dialog_SmdLibrary), so the common path never needs Mod settings.
            r.Page(SmdLibraryUi.PageLibrary, "My defaults", SmdLibraryUi.DrawLibrary).WithOrder(5);
            r.Page("policies", "Policy set", SmdPages.DrawPolicies).WithOrder(45).InSection(SmdSections.Policies);
            r.Page("colony", "Colony defaults", SmdPages.DrawColony).WithOrder(55).InSection(SmdSections.Colony);
            r.Page(SmdLibraryUi.PageProfiles, "Profiles", SmdLibraryUi.DrawProfiles).WithOrder(65).InSection(SmdSections.Library);
            r.Page(SmdLibraryUi.PageShare, "Import and export", SmdLibraryUi.DrawShare).WithOrder(66).InSection(SmdSections.Library);

            // ---------------------------------------------------------------- per save data (removal-safe: inside the framework's component)
            r.SaveData(SmdGameData.Expose, SmdGameData.Reset);
        }

        /// <summary>Settings that make things happen by themselves (the presets switch them together).</summary>
        static readonly string[] AutoKeys =
        {
            "smd.autoApplyOnPlace", "smd.storageAutoApplyOnBuild", "smd.plantAutoApplyOnBuild",
            "smd.zoneAutoApplyOnCreate", "smd.growingAutoApplyOnCreate", "smd.autoApplyToNewPawns",
        };

        /// <summary>The mod was added to a running save: its welcome card offers the "what happens by itself" choice once.</summary>
        protected override void OnGameLoaded(bool joinedExistingSave)
        {
            if (joinedExistingSave && SmdGameData.startCard == SmdGameData.StartNone)
                SmdGameData.startCard = SmdGameData.StartJoined;
        }

        /// <summary>One "what happens by itself" choice: a framework preset (Overview) and a card on this mod's welcome card.</summary>
        public sealed class Mode
        {
            public string Id, LabelEn, DescEn;
            public Action<KitProfile> Apply;
            public Func<KitProfile, bool> Matches;
            public string Label => Tr("SMD.Preset." + Id, LabelEn);
            public string Desc => Tr("SMD.Preset." + Id + ".Desc", DescEn);
            static string Tr(string key, string en) => key.CanTranslate() ? key.Translate().ToString() : en;
        }

        static bool AllAuto(KitProfile p, bool on) => AutoKeys.All(key => p.GetBool(key) == on);

        /// <summary>Automatic (recommended) / Only when I click Apply / Buildings and zones only.</summary>
        public static readonly List<Mode> Modes = new List<Mode>
        {
            new Mode
            {
                Id = "automatic", LabelEn = "Automatic (recommended)",
                DescEn = "Everything on: new workbenches, shelves, zones and colonists get their defaults by themselves.",
                Apply = p => { foreach (var id in SmdSections.All) p.SetSection(id, true); foreach (var key in AutoKeys) p.Set(key, true); },
                Matches = p => SmdSections.All.All(p.SectionOn) && AllAuto(p, true),
            },
            new Mode
            {
                Id = "manual", LabelEn = "Only when I click Apply",
                DescEn = "Nothing happens by itself; you apply presets with the Defaults button.",
                Apply = p => { foreach (var id in SmdSections.All) p.SetSection(id, true); foreach (var key in AutoKeys) p.Set(key, false); },
                Matches = p => SmdSections.All.All(p.SectionOn) && AllAuto(p, false),
            },
            new Mode
            {
                Id = "buildings", LabelEn = "Buildings and zones only",
                DescEn = "Bills, shelves and zones; colonists, policies and colony settings are left alone.",
                Apply = p =>
                {
                    p.SetSection(SmdSections.Buildings, true); p.SetSection(SmdSections.Zones, true); p.SetSection(SmdSections.Library, true);
                    p.SetSection(SmdSections.Pawns, false); p.SetSection(SmdSections.Policies, false); p.SetSection(SmdSections.Colony, false);
                    foreach (var key in AutoKeys) p.Set(key, true);
                },
                Matches = p => p.SectionOn(SmdSections.Buildings) && p.SectionOn(SmdSections.Zones) && !p.SectionOn(SmdSections.Pawns)
                    && !p.SectionOn(SmdSections.Policies) && !p.SectionOn(SmdSections.Colony) && AllAuto(p, true),
            },
        };

        protected override void OnDefsLoaded()
        {
            KitKnown.Declare(Kit, KitTags.ChangesBillsUi, KitTags.ChangesBills, KitTags.ChangesStorage, KitTags.ChangesWork, KitTags.ChangesPawnUi);
        }

        protected override IEnumerable<string> Diagnosis()
        {
            yield return "Bill limit: " + (BillCapCompat.Active ? "owned by No Max Bills" : BillCapPatched ? "own transpiler in place, now " + BillCap.Current() : "vanilla (transpiler not applied)");
            yield return "Building spawn hook: " + (Patch_SpawnSetup.Applied ? "applied" : "NOT applied");
        }

        static string SafeStatus(Func<LibraryStatus, object> f)
        {
            try { return f(SmdLibraryApi.Status())?.ToString() ?? "?"; }
            catch (Exception e) { return "unavailable (" + e.GetType().Name + ")"; }
        }

        static IEnumerable<string> LibraryDiagnosis()
        {
            var lines = new List<string>();
            try
            {
                var s = SmdLibraryApi.Status();
                lines.Add("Library: " + (s.readOnly ? "READ-ONLY (" + s.loadError + ")" : "writable") + ", profiles " + s.profiles);
                if (s.lastMigration != null) lines.Add("Imported from 1.x this session: " + s.lastMigration);
                var miss = SmdLibraryApi.MissingInLibrary();
                if (!miss.IsEmpty) lines.Add("Presets that need missing mods: " + miss.Summary());
            }
            catch (Exception e) { lines.Add("Library status unavailable: " + e.GetType().Name); }
            return lines;
        }

        /// <summary>Manually patch the two vanilla bill-UI methods via our transpiler. Any failure is logged and swallowed.</summary>
        private static void TryPatchBillCap(Harmony harmony)
        {
            try
            {
                var transpiler = new HarmonyMethod(typeof(Patch_BillStack_RaiseCap), nameof(Patch_BillStack_RaiseCap.Transpiler));
                int ok = 0;
                foreach (var target in Patch_BillStack_RaiseCap.TargetMethods())
                {
                    try { harmony.Patch(target, transpiler: transpiler); ok++; }
                    catch (Exception e) { WBD_Log.Warn("Could not raise the bill limit on " + target?.Name + " (the limit stays 15 there): " + e.Message); }
                }
                BillCapPatched = ok > 0;
            }
            catch (Exception e)
            {
                WBD_Log.Warn("The bill-limit patch could not be registered (the limit stays 15): " + e.Message);
            }
        }

        /// <summary>Opens this mod's settings on a page ("page:colonistPresets", "section:pawns", "overview").</summary>
        public static void OpenSettings(string pageId)
        {
            var k = K;
            if (k == null) return;
            if (string.IsNullOrEmpty(pageId)) k.OpenSettings(); else k.OpenSettings(pageId);
        }

        /// <summary>Old entry point of the pawn "Manage" action: the in-game library window on the colonist presets.</summary>
        public static void OpenSettingsPawnTab() => Dialog_SmdLibrary.Open(SmdLibraryUi.PageLibrary, "Pawns");
    }

    /// <summary>
    /// The settings class (old name kept: it is written into the settings file). Since 2.0 every value is a framework registration;
    /// the properties below keep the old field names so the rest of the code reads them unchanged. Old files are migrated by the
    /// framework (every registration carries <c>Legacy("oldField")</c>, every area <c>LegacyEnabled("sectionX")</c>).
    /// </summary>
    public sealed class WBD_Settings : KitSettingsBase
    {
        bool B(string key, bool def) { var k = Kit; return k != null ? k.GetBool(key) : def; }
        void SetB(string key, bool v) => Kit?.Set(key, v);

        // Buildings
        public bool autoApplyOnPlace { get => B("smd.autoApplyOnPlace", true); set => SetB("smd.autoApplyOnPlace", value); }
        public bool showTabButton { get => B("smd.showTabButton", true); set => SetB("smd.showTabButton", value); }
        public bool showGizmo { get => B("smd.showGizmo", true); set => SetB("smd.showGizmo", value); }

        // Pawns
        public bool autoApplyToNewPawns { get => B("smd.autoApplyToNewPawns", true); set => SetB("smd.autoApplyToNewPawns", value); }
        public bool pawnShowGizmo { get => B("smd.pawnShowGizmo", true); set => SetB("smd.pawnShowGizmo", value); }
        public bool pawnIncludeChildren { get => B("smd.pawnIncludeChildren", true); set => SetB("smd.pawnIncludeChildren", value); }
        public bool pawnSkipWorkUnderFreeWill { get => B("smd.pawnSkipWorkUnderFreeWill", true); set => SetB("smd.pawnSkipWorkUnderFreeWill", value); }
        public bool pawnApplySchedule { get => B("smd.pawnApplySchedule", true); set => SetB("smd.pawnApplySchedule", value); }
        public bool pawnApplyWork { get => B("smd.pawnApplyWork", true); set => SetB("smd.pawnApplyWork", value); }
        public bool pawnApplyApparel { get => B("smd.pawnApplyApparel", true); set => SetB("smd.pawnApplyApparel", value); }
        public bool pawnApplyDrug { get => B("smd.pawnApplyDrug", true); set => SetB("smd.pawnApplyDrug", value); }
        public bool pawnApplyFood { get => B("smd.pawnApplyFood", true); set => SetB("smd.pawnApplyFood", value); }
        public bool pawnApplyReading { get => B("smd.pawnApplyReading", true); set => SetB("smd.pawnApplyReading", value); }
        public bool pawnApplyHostility { get => B("smd.pawnApplyHostility", true); set => SetB("smd.pawnApplyHostility", value); }
        public bool pawnApplyMedical { get => B("smd.pawnApplyMedical", true); set => SetB("smd.pawnApplyMedical", value); }

        // Bills / policies
        public bool nativeRaiseBillCap { get => B("smd.nativeRaiseBillCap", false); set => SetB("smd.nativeRaiseBillCap", value); }
        public int maxBillsCap
        {
            get { var k = Kit; return k != null ? Mathf.Clamp(k.GetInt("smd.maxBillsCap"), BillStack.MaxCount, 250) : 50; }
            set => Kit?.Set("smd.maxBillsCap", (float)Mathf.Clamp(value, BillStack.MaxCount, 250));
        }
        public bool policyAutoApplyOnNewGame { get => B("smd.policyAutoApplyOnNewGame", false); set => SetB("smd.policyAutoApplyOnNewGame", value); }

        // Buildings / storage / zones
        public bool storageAutoApplyOnBuild { get => B("smd.storageAutoApplyOnBuild", true); set => SetB("smd.storageAutoApplyOnBuild", value); }
        public bool plantAutoApplyOnBuild { get => B("smd.plantAutoApplyOnBuild", true); set => SetB("smd.plantAutoApplyOnBuild", value); }
        public bool zoneAutoApplyOnCreate { get => B("smd.zoneAutoApplyOnCreate", true); set => SetB("smd.zoneAutoApplyOnCreate", value); }
        public bool growingAutoApplyOnCreate { get => B("smd.growingAutoApplyOnCreate", true); set => SetB("smd.growingAutoApplyOnCreate", value); }
        public bool benchFamilies { get => B("smd.benchFamilies", true); set => SetB("smd.benchFamilies", value); }
        public bool filterIntent { get => B("smd.filterIntent", true); set => SetB("smd.filterIntent", value); }
        public bool billMatchStockpileByName { get => B("smd.billMatchStockpileByName", true); set => SetB("smd.billMatchStockpileByName", value); }

        // Colony
        public bool colonyApplyOnNewGame { get => B("smd.colonyApplyOnNewGame", true); set => SetB("smd.colonyApplyOnNewGame", value); }

        // Compatibility
        public bool compatNiceBillTab => B("smd.compatNiceBillTab", true);
        public bool compatPrf => B("smd.compatPrf", true);
        public bool compatBlueprints => B("smd.compatBlueprints", true);

        /// <summary>
        /// The effective per-workbench bill cap: the configured cap whenever the built-in raiser is on OR an external "No Max Bills"
        /// mod is present (so the visible UI cap and the number of bills we restore agree); otherwise vanilla's 15.
        /// </summary>
        public int EffectiveBillCap()
        {
            if (BillCapCompat.Active)
                return Mathf.Max(maxBillsCap, BillStack.MaxCount);
            return BillCap.Current();
        }

        /// <summary>The set of pawn axes the player currently has enabled for auto-apply.</summary>
        public PawnAxis EnabledPawnAxes()
        {
            PawnAxis a = PawnAxis.None;
            if (pawnApplySchedule) a |= PawnAxis.Schedule;
            if (pawnApplyWork) a |= PawnAxis.Work;
            if (pawnApplyApparel) a |= PawnAxis.Apparel;
            if (pawnApplyDrug) a |= PawnAxis.Drug;
            if (pawnApplyFood) a |= PawnAxis.Food;
            if (pawnApplyReading) a |= PawnAxis.Reading;
            if (pawnApplyHostility) a |= PawnAxis.Hostility;
            if (pawnApplyMedical) a |= PawnAxis.Medical;
            return a;
        }
    }

    /// <summary>Soft integrations that several places call.</summary>
    public static class SmdCompat
    {
        /// <summary>Nice Bill Tab caches its bill list: ask it to rebuild after bills changed from code (framework adapter).</summary>
        public static void RefreshBillsTab()
        {
            var s = WBD_Mod.Settings;
            if (s != null && !s.compatNiceBillTab) return;
            if (KitNiceBillTab.Active) { KitNiceBillTab.RequestRefresh(); return; }
            // FW-REQUEST: KitNiceBillTab gates on the packageId being active. Nice Bill Tab has no 1.7 build; its 1.6 build runs on 1.7
            // ("incompatible version"), and then ModsConfig/ModLister report it as NOT active although its code is loaded, so the
            // adapter never refreshes. Fallback: find its refresh flag by type, as 1.x did (once, cached).
            if (!nbtResolved)
            {
                nbtResolved = true;
                try
                {
                    var f = AccessTools.Field(AccessTools.TypeByName("NiceBillTab.TabBillsDrawer"), "shouldRefreshFilter");
                    if (f != null && f.IsStatic && f.FieldType == typeof(bool)) nbtFlag = f;
                }
                catch { nbtFlag = null; }
            }
            if (nbtFlag == null) return;
            try { nbtFlag.SetValue(null, true); }
            catch (Exception e) { WBD_Log.Warn("Nice Bill Tab refresh failed (bills appear after reopening the tab): " + e.Message); nbtFlag = null; }
        }

        /// <summary>Nice Bill Tab's code is loaded (by the framework adapter or the type fallback).</summary>
        public static bool NiceBillTabLoaded
        {
            get
            {
                if (KitNiceBillTab.Active) return true;
                try { return AccessTools.TypeByName("NiceBillTab.TabBillsDrawer") != null; } catch { return false; }
            }
        }

        static bool nbtResolved;
        static System.Reflection.FieldInfo nbtFlag;
    }

    /// <summary>Tiny logging helper so every message is greppable and prefixed.</summary>
    public static class WBD_Log
    {
        private const string Prefix = "[Set my Defaults] ";
        public static void Msg(string m) => Log.Message(Prefix + m);
        public static void Warn(string m) => Log.Warning(Prefix + m);
        public static void Error(string m) => Log.Error(Prefix + m);
    }
}
