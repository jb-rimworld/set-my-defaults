using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Mod entry point. Holds settings and bootstraps Harmony patches exactly once.
    /// Kept deliberately small: all heavy lifting lives in the static helpers, and no
    /// per-tick or per-frame work is introduced beyond a couple of cheap guarded patches.
    /// </summary>
    public class WBD_Mod : Mod
    {
        public static WBD_Settings Settings { get; private set; }

        /// <summary>True once the live bill-cap transpiler is in place (false when No Max Bills owns the limit or the patch failed).</summary>
        public static bool BillCapPatched { get; private set; }

        public WBD_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WBD_Settings>();

            var harmony = new Harmony("juliusberger.workbenchbilldefaults");
            // Apply each patch independently so one failing patch can never disable the
            // others (and never aborts mod init with an unhandled exception).
            TryPatch(harmony, typeof(Patch_Building_GetGizmos));
            TryPatch(harmony, typeof(Patch_ITab_Bills_FillTab));
            // Manual patch for SpawnSetup (resolved defensively so a future API change
            // degrades to "feature off" instead of a hard crash).
            try
            {
                Patch_SpawnSetup.Apply(harmony);
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to apply SpawnSetup patch: " + e);
            }

            // Pawn-defaults feature (schedule / work / policies / hostility / medical).
            TryPatch(harmony, typeof(Patch_Pawn_GetGizmos));
            TryPatch(harmony, typeof(Patch_Pawn_SpawnSetup));
            TryPatch(harmony, typeof(Patch_Pawn_SetFaction));

            // Native >15 bills: register the self-contained bill-cap transpiler whenever no external
            // "No Max Bills" mod already raised the cap (interop, no double patch). The patched code reads
            // BillCap.Current() live, so the setting works without a restart (2.0, M3). Wrapped so a future
            // vanilla IL change degrades to "cap feature off".
            if (!BillCapCompat.Active)
                TryPatchBillCap(harmony);
        }

        /// <summary>
        /// Manually patch the two vanilla bill-UI methods via our transpiler (the patch class carries no
        /// HarmonyPatch attribute, so it is never auto-applied). Any failure is logged and swallowed,
        /// leaving every other patch — and the internal EffectiveBillCap clamp — fully functional.
        /// </summary>
        private static void TryPatchBillCap(Harmony harmony)
        {
            try
            {
                var transpiler = new HarmonyMethod(typeof(Patch_BillStack_RaiseCap), nameof(Patch_BillStack_RaiseCap.Transpiler));
                int ok = 0;
                foreach (var target in Patch_BillStack_RaiseCap.TargetMethods())
                {
                    try
                    {
                        harmony.Patch(target, transpiler: transpiler);
                        ok++;
                    }
                    catch (Exception e)
                    {
                        WBD_Log.Error("Failed to raise bill cap on " + target?.Name + ": " + e);
                    }
                }
                BillCapPatched = ok > 0;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to register the bill-cap transpiler: " + e);
            }
        }

        private static void TryPatch(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to apply patch " + patchClass.Name + ": " + e);
            }
        }

        public override string SettingsCategory() => "Set my Defaults";

        private enum SettingsTab { Buildings, Pawns, Policies }
        private SettingsTab _tab = SettingsTab.Buildings;
        private Vector2 _scrollPos;
        private float _viewHeight = 300f;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            // Tab row (three tabs). Switching resets the scroll position and the cached content height; keeping
            // the previous tab's values left the new tab scrolled past its end (an empty/broken panel).
            var tabBefore = _tab;
            float third = (inRect.width - 16f) / 3f;
            if (DrawTabButton(new Rect(inRect.x, inRect.y, third, 30f), "WBD_TabBuildings".Translate(), _tab == SettingsTab.Buildings))
                _tab = SettingsTab.Buildings;
            if (DrawTabButton(new Rect(inRect.x + third + 8f, inRect.y, third, 30f), "WBD_TabPawns".Translate(), _tab == SettingsTab.Pawns))
                _tab = SettingsTab.Pawns;
            if (DrawTabButton(new Rect(inRect.x + 2f * (third + 8f), inRect.y, third, 30f), "WBD_TabPolicies".Translate(), _tab == SettingsTab.Policies))
                _tab = SettingsTab.Policies;
            if (_tab != tabBefore)
            {
                _scrollPos = Vector2.zero;
                _viewHeight = 300f;
            }

            // Scrollable body holding the active tab's toggles + preset overview.
            var body = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 38f);
            var viewRect = new Rect(0f, 0f, body.width - 20f, _viewHeight);
            Widgets.BeginScrollView(body, ref _scrollPos, viewRect);
            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(viewRect);
            try
            {
                if (_tab == SettingsTab.Buildings)
                    DrawBuildingTab(listing);
                else if (_tab == SettingsTab.Pawns)
                    DrawPawnTab(listing);
                else
                    DrawPolicyTab(listing);
            }
            catch (System.Exception e)
            {
                // A throw in the middle of a tab must not leave the scroll view/listing open: that corrupts the
                // GUI group stack and breaks every later tab.
                WBD_Log.Warn("Settings tab " + _tab + " failed to draw: " + e);
            }
            finally
            {
                _viewHeight = Mathf.Max(listing.CurHeight, 50f);
                listing.End();
                Widgets.EndScrollView();
            }
        }

        private static bool DrawTabButton(Rect rect, string label, bool active)
        {
            if (active)
                Widgets.DrawHighlight(rect);
            return Widgets.ButtonText(rect, label);
        }

        // ---- Buildings tab ----

        private void DrawBuildingTab(Listing_Standard list)
        {
            list.CheckboxLabeled("WBD_SettingAutoApply".Translate(), ref Settings.autoApplyOnPlace);
            list.CheckboxLabeled("WBD_SettingShowTabButton".Translate(), ref Settings.showTabButton);
            list.CheckboxLabeled("WBD_SettingShowGizmo".Translate(), ref Settings.showGizmo);
            list.GapLine();
            list.Label("WBD_OverviewHeader".Translate());

            var defNames = DefaultBillsDatabase.AllSavedDefNames();
            var rows = defNames
                .Select(dn => new { defName = dn, def = DefDatabase<ThingDef>.GetNamedSilentFail(dn), presets = DefaultBillsDatabase.GetPresets(dn) })
                .Where(r => r.presets != null && !r.presets.IsEmpty)
                .OrderBy(r => (r.def != null ? r.def.label : r.defName))
                .ToList();
            if (rows.Count == 0)
            {
                list.Label("WBD_OverviewEmpty".Translate());
                return;
            }

            foreach (var row in rows)
            {
                var header = list.GetRect(28f);
                var labelText = row.def != null
                    ? row.def.LabelCap.ToString()
                    : "WBD_MissingDef".Translate(row.defName).ToString();
                Widgets.Label(new Rect(header.x, header.y + 3f, header.width - 96f, 24f), labelText.Truncate(header.width - 100f));
                if (Widgets.ButtonText(new Rect(header.xMax - 90f, header.y + 1f, 90f, 26f), "WBD_DeleteAll".Translate()))
                    DefaultBillsDatabase.ClearAll(row.defName);
                Widgets.DrawLineHorizontal(header.x, header.yMax, header.width);

                foreach (var preset in row.presets.presets.ToList())
                {
                    var name = preset.name;
                    bool isDefault = row.presets.IsDefault(name);
                    var r = list.GetRect(28f);

                    if (Widgets.RadioButton(r.x + 12f, r.y + 2f, isDefault))
                        DefaultBillsDatabase.SetDefaultPreset(row.defName, name);
                    var nameRect = new Rect(r.x + 46f, r.y + 4f, r.width - 46f - 156f, 24f);
                    var shown = isDefault ? "WBD_PresetIsDefault".Translate(name).ToString() : name;
                    Widgets.Label(nameRect, shown.Truncate(nameRect.width));

                    if (Widgets.ButtonText(new Rect(r.xMax - 150f, r.y + 2f, 72f, 24f), "WBD_Rename".Translate()))
                        PromptRename(row.defName, name);
                    if (Widgets.ButtonText(new Rect(r.xMax - 74f, r.y + 2f, 72f, 24f), "WBD_Delete".Translate()))
                        DefaultBillsDatabase.RemovePreset(row.defName, name);
                }

                list.Gap(10f);
            }
        }

        private static void PromptRename(string defName, string oldName)
        {
            Find.WindowStack.Add(new WBD_Dialog_NamePreset(
                "WBD_RenameTitle".Translate(oldName),
                oldName,
                newName =>
                {
                    if (!DefaultBillsDatabase.RenamePreset(defName, oldName, newName))
                        Messages.Message("WBD_RenameFailed".Translate(newName), MessageTypeDefOf.RejectInput, false);
                }));
        }

        // ---- Pawns tab ----

        private void DrawPawnTab(Listing_Standard list)
        {
            list.CheckboxLabeled("WBD_PawnAutoApply".Translate(), ref Settings.autoApplyToNewPawns);
            list.CheckboxLabeled("WBD_PawnShowGizmo".Translate(), ref Settings.pawnShowGizmo);
            list.CheckboxLabeled("WBD_PawnIncludeChildren".Translate(), ref Settings.pawnIncludeChildren);

            list.GapLine();
            list.Label("WBD_PawnAxesHeader".Translate());
            list.CheckboxLabeled("WBD_AxisSchedule".Translate(), ref Settings.pawnApplySchedule);
            list.CheckboxLabeled("WBD_AxisWork".Translate(), ref Settings.pawnApplyWork);
            if (Settings.pawnApplyWork && FreeWillCompat.Active)
            {
                list.CheckboxLabeled("WBD_PawnSkipWorkFreeWill".Translate(), ref Settings.pawnSkipWorkUnderFreeWill);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                list.Label("WBD_PawnFreeWillNote".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            list.CheckboxLabeled("WBD_AxisApparel".Translate(), ref Settings.pawnApplyApparel);
            list.CheckboxLabeled("WBD_AxisDrug".Translate(), ref Settings.pawnApplyDrug);
            list.CheckboxLabeled("WBD_AxisFood".Translate(), ref Settings.pawnApplyFood);
            list.CheckboxLabeled("WBD_AxisReading".Translate(), ref Settings.pawnApplyReading);
            list.CheckboxLabeled("WBD_AxisHostility".Translate(), ref Settings.pawnApplyHostility);
            list.CheckboxLabeled("WBD_AxisMedical".Translate(), ref Settings.pawnApplyMedical);

            list.GapLine();
            list.Label("WBD_PawnOverviewHeader".Translate());

            var presets = PawnDefaultsStore.GetPresets();
            if (presets == null || presets.IsEmpty)
            {
                list.Label("WBD_PawnOverviewEmpty".Translate());
                return;
            }

            foreach (var preset in presets.presets.ToList())
            {
                var name = preset.name;
                bool isDefault = presets.IsDefault(name);
                var r = list.GetRect(28f);

                if (Widgets.RadioButton(r.x + 6f, r.y + 2f, isDefault))
                    PawnDefaultsStore.SetDefaultPreset(name);
                var nameRect = new Rect(r.x + 40f, r.y + 4f, r.width - 40f - 156f, 24f);
                var shown = isDefault ? "WBD_PresetIsDefault".Translate(name).ToString() : name;
                Widgets.Label(nameRect, shown.Truncate(nameRect.width));

                if (Widgets.ButtonText(new Rect(r.xMax - 150f, r.y + 2f, 72f, 24f), "WBD_Rename".Translate()))
                    PromptRenamePawn(name);
                if (Widgets.ButtonText(new Rect(r.xMax - 74f, r.y + 2f, 72f, 24f), "WBD_Delete".Translate()))
                    PawnDefaultsStore.RemovePreset(name);
            }
        }

        private static void PromptRenamePawn(string oldName)
        {
            Find.WindowStack.Add(new WBD_Dialog_NamePreset(
                "WBD_RenameTitle".Translate(oldName),
                oldName,
                newName =>
                {
                    if (!PawnDefaultsStore.RenamePreset(oldName, newName))
                        Messages.Message("WBD_RenameFailed".Translate(newName), MessageTypeDefOf.RejectInput, false);
                }));
        }

        // ---- Policies tab ----

        private void DrawPolicyTab(Listing_Standard list)
        {
            // --- Native >15 bills ---
            list.Label("WBD_BillCapHeader".Translate());
            if (BillCapCompat.Active)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                list.Label("WBD_BillCapExternal".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else
            {
                list.CheckboxLabeled("WBD_SettingNativeBillCap".Translate(), ref Settings.nativeRaiseBillCap);
            }

            if (Settings.nativeRaiseBillCap || BillCapCompat.Active)
            {
                list.Label("WBD_SettingBillCapValue".Translate(Settings.maxBillsCap));
                Settings.maxBillsCap = Mathf.RoundToInt(list.Slider(Settings.maxBillsCap, 15f, 250f));
            }
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            list.Label("WBD_BillCapNote".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            list.GapLine();

            // --- Cross-save policy defaults ---
            list.Label("WBD_PolicyDefaultsHeader".Translate());

            if (!PolicyDefaultsController.GameReady)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                list.Label("WBD_PolicyNoGame".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else
            {
                var btnRow = list.GetRect(30f);
                float bw = (btnRow.width - 8f) / 2f;
                if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, bw, 28f), "WBD_SaveDefaultPolicies".Translate()))
                {
                    if (PolicyDefaultsStore.SaveCurrentColony())
                        Messages.Message("WBD_PolicyCrossSaveSaved".Translate(), MessageTypeDefOf.TaskCompletion, false);
                    else
                        Messages.Message("WBD_SaveBusy".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                bool hasSet = PolicyDefaultsStore.HasSet();
                if (Widgets.ButtonText(new Rect(btnRow.x + bw + 8f, btnRow.y, bw, 28f), "WBD_ApplyDefaultPolicies".Translate()) && hasSet)
                {
                    if (PolicyDefaultsStore.ApplyToCurrentColony())
                        Messages.Message("WBD_PolicyCrossSaveApplied".Translate(), MessageTypeDefOf.TaskCompletion, false);
                    else
                        Messages.Message("WBD_SaveBusy".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }

            list.CheckboxLabeled("WBD_PolicyAutoApply".Translate(), ref Settings.policyAutoApplyOnNewGame);

            // Saved-set overview.
            var savedSet = PolicyDefaultsStore.GetSet();
            if (savedSet != null && !savedSet.IsEmpty)
            {
                list.Gap(4f);
                list.Label("WBD_PolicySavedSetHeader".Translate());
                foreach (var kind in PolicyDefaultsController.AllKinds)
                {
                    var saved = savedSet.ListFor(kind);
                    if (saved == null || saved.Count == 0)
                        continue;
                    var def = savedSet.DefaultLabelFor(kind);
                    var labels = saved.Select(t => t.label == def ? "★ " + t.label : t.label).ToList();
                    list.Label("  " + PolicyDefaultsController.KindLabel(kind) + ": " + string.Join(", ", labels));
                }
                var clearRow = list.GetRect(28f);
                if (Widgets.ButtonText(new Rect(clearRow.x, clearRow.y, 160f, 26f), "WBD_PolicyClearSaved".Translate()))
                    PolicyDefaultsStore.ClearAll();
            }

            list.GapLine();

            // --- Granular per-kind colony default (live databases) ---
            list.Label("WBD_PolicyColonyDefaultsHeader".Translate());
            if (!PolicyDefaultsController.GameReady)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                list.Label("WBD_PolicyNoGame".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }

            foreach (var kind in PolicyDefaultsController.AllKinds)
                DrawPolicyKindSection(list, kind);
        }

        private void DrawPolicyKindSection(Listing_Standard list, PolicyKind kind)
        {
            var header = list.GetRect(28f);
            Widgets.Label(new Rect(header.x, header.y + 3f, header.width - 130f, 24f),
                PolicyDefaultsController.KindLabel(kind));
            if (Widgets.ButtonText(new Rect(header.xMax - 124f, header.y + 1f, 124f, 26f), "WBD_PolicyManage".Translate()))
                PolicyDefaultsController.OpenManageDialog(kind);
            Widgets.DrawLineHorizontal(header.x, header.yMax, header.width);

            var currentDefault = PolicyDefaultsController.DefaultPolicy(kind);
            foreach (var live in PolicyDefaultsController.LivePolicies(kind).ToList())
            {
                var name = PolicyDefaultsController.PolicyLabel(live) ?? "?";
                bool isDefault = ReferenceEquals(live, currentDefault);
                var r = list.GetRect(28f);
                if (Widgets.RadioButton(r.x + 12f, r.y + 2f, isDefault) && !isDefault)
                {
                    if (PolicyDefaultsController.SetColonyDefault(kind, live))
                        Messages.Message("WBD_PolicyDefaultSet".Translate(name), MessageTypeDefOf.TaskCompletion, false);
                }
                var nameRect = new Rect(r.x + 46f, r.y + 4f, r.width - 46f, 24f);
                var shown = isDefault ? "WBD_PresetIsDefault".Translate(name).ToString() : name;
                Widgets.Label(nameRect, shown.Truncate(nameRect.width));
            }
            list.Gap(10f);
        }

        /// <summary>Open mod settings with the Pawns tab pre-selected (used by the pawn "Manage" action).</summary>
        public static void OpenSettingsPawnTab()
        {
            var mod = LoadedModManager.GetMod<WBD_Mod>();
            if (mod == null)
                return;
            mod._tab = SettingsTab.Pawns;
            Find.WindowStack.Add(new Dialog_ModSettings(mod));
        }
    }

    public class WBD_Settings : ModSettings
    {
        // Buildings
        public bool autoApplyOnPlace = true;
        public bool showTabButton = true;
        public bool showGizmo = true;

        // Pawns
        public bool autoApplyToNewPawns = true;
        public bool pawnShowGizmo = true;
        public bool pawnIncludeChildren = true;
        public bool pawnSkipWorkUnderFreeWill = true;
        public bool pawnApplySchedule = true;
        public bool pawnApplyWork = true;
        public bool pawnApplyApparel = true;
        public bool pawnApplyDrug = true;
        public bool pawnApplyFood = true;
        public bool pawnApplyReading = true;
        public bool pawnApplyHostility = true;
        public bool pawnApplyMedical = true;

        // Policies / bills
        public bool nativeRaiseBillCap = false;   // master toggle for the self-contained bill-cap transpiler
        public int maxBillsCap = 50;              // configurable cap (15-250); also the UI literal the transpiler writes
        public bool policyAutoApplyOnNewGame = false; // optional: apply saved policy defaults once per new game

        // ---- Feature areas (2.0). Each one switches a whole area off; saved data is kept. Read only through
        // SmdSections.On(id), never directly, so the kit can take these over in one place. ----
        public bool sectionBuildings = true;   // workbench bills + building storage/plant presets, gizmo, bills-tab button
        public bool sectionZones = true;       // stockpile and growing zone presets (gizmo + auto-apply on new zones)
        public bool sectionPawns = true;       // colonist presets (gizmo + auto-apply to new colonists)
        public bool sectionPolicies = true;    // cross-save outfit/drug/food/reading policy set
        public bool sectionColony = true;      // cross-save colony settings (medical defaults, auto home, auto rebuild, manual priorities)
        public bool sectionLibrary = true;     // profiles, share strings, import/export

        // ---- Buildings / storage / zones (2.0) ----
        public bool storageAutoApplyOnBuild = true;  // shelves and other storage get their default filter when newly constructed
        public bool plantAutoApplyOnBuild = true;    // hydroponics and other plant growers get their default plant when newly constructed
        public bool zoneAutoApplyOnCreate = true;    // new stockpile / dumping zones get their default filter
        public bool growingAutoApplyOnCreate = true; // new growing zones get their default plant (+ sow/cut toggles)
        public bool benchFamilies = true;            // a building without its own preset uses the default of a closely related building
        public bool filterIntent = true;             // filters remember categories + exceptions, so newly added items of a category are included
        public bool billMatchStockpileByName = true; // "specific stockpile" (store in / count in) is restored when a stockpile of that name exists

        // ---- Colony (2.0) ----
        public bool colonyApplyOnNewGame = true;     // apply the saved colony settings when a new game starts (only if any are saved)

        /// <summary>
        /// The effective per-workbench bill cap. Returns the player's configured cap whenever the
        /// built-in raiser is on OR an external "No Max Bills" mod is present (so the visible UI cap and
        /// the number of bills we restore always agree); otherwise vanilla's 15.
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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref autoApplyOnPlace, "autoApplyOnPlace", true);
            Scribe_Values.Look(ref showTabButton, "showTabButton", true);
            Scribe_Values.Look(ref showGizmo, "showGizmo", true);

            Scribe_Values.Look(ref autoApplyToNewPawns, "autoApplyToNewPawns", true);
            Scribe_Values.Look(ref pawnShowGizmo, "pawnShowGizmo", true);
            Scribe_Values.Look(ref pawnIncludeChildren, "pawnIncludeChildren", true);
            Scribe_Values.Look(ref pawnSkipWorkUnderFreeWill, "pawnSkipWorkUnderFreeWill", true);
            Scribe_Values.Look(ref pawnApplySchedule, "pawnApplySchedule", true);
            Scribe_Values.Look(ref pawnApplyWork, "pawnApplyWork", true);
            Scribe_Values.Look(ref pawnApplyApparel, "pawnApplyApparel", true);
            Scribe_Values.Look(ref pawnApplyDrug, "pawnApplyDrug", true);
            Scribe_Values.Look(ref pawnApplyFood, "pawnApplyFood", true);
            Scribe_Values.Look(ref pawnApplyReading, "pawnApplyReading", true);
            Scribe_Values.Look(ref pawnApplyHostility, "pawnApplyHostility", true);
            Scribe_Values.Look(ref pawnApplyMedical, "pawnApplyMedical", true);

            Scribe_Values.Look(ref nativeRaiseBillCap, "nativeRaiseBillCap", false);
            Scribe_Values.Look(ref maxBillsCap, "maxBillsCap", 50);
            Scribe_Values.Look(ref policyAutoApplyOnNewGame, "policyAutoApplyOnNewGame", false);

            Scribe_Values.Look(ref sectionBuildings, "sectionBuildings", true);
            Scribe_Values.Look(ref sectionZones, "sectionZones", true);
            Scribe_Values.Look(ref sectionPawns, "sectionPawns", true);
            Scribe_Values.Look(ref sectionPolicies, "sectionPolicies", true);
            Scribe_Values.Look(ref sectionColony, "sectionColony", true);
            Scribe_Values.Look(ref sectionLibrary, "sectionLibrary", true);

            Scribe_Values.Look(ref storageAutoApplyOnBuild, "storageAutoApplyOnBuild", true);
            Scribe_Values.Look(ref plantAutoApplyOnBuild, "plantAutoApplyOnBuild", true);
            Scribe_Values.Look(ref zoneAutoApplyOnCreate, "zoneAutoApplyOnCreate", true);
            Scribe_Values.Look(ref growingAutoApplyOnCreate, "growingAutoApplyOnCreate", true);
            Scribe_Values.Look(ref benchFamilies, "benchFamilies", true);
            Scribe_Values.Look(ref filterIntent, "filterIntent", true);
            Scribe_Values.Look(ref billMatchStockpileByName, "billMatchStockpileByName", true);

            Scribe_Values.Look(ref colonyApplyOnNewGame, "colonyApplyOnNewGame", true);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
                maxBillsCap = Mathf.Clamp(maxBillsCap, BillStack.MaxCount, 250);
        }
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
