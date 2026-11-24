using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The custom pages of the settings window (JB Framework draws sidebar, search, switches and plain settings; these pages show
    /// what is not a plain value: the saved presets, the policy set, the colony settings, the library status). Every page scrolls
    /// itself. The framework runs each draw inside a UI fence.
    /// </summary>
    public static class SmdPages
    {
        /// <summary>
        /// Hook for the library / profiles page (next wave: profile list, preview, share strings). When set, it replaces the
        /// built-in status page. Signature: draw into the given rect, scrolling yourself.
        /// </summary>
        public static Action<Rect> LibraryPage;

        // ------------------------------------------------------------------------------------------------ scrolling helper
        sealed class Scroll { public Vector2 pos; public float height = 400f; }
        static readonly Scroll sBuildings = new Scroll(), sColonists = new Scroll(), sPolicies = new Scroll(), sColony = new Scroll(), sLibrary = new Scroll();

        static void Scrolled(Rect area, Scroll s, Action<Listing_Standard> body)
        {
            Text.Font = GameFont.Small;
            var view = new Rect(0f, 0f, area.width - 20f, Mathf.Max(s.height, area.height - 4f));
            Widgets.BeginScrollView(area, ref s.pos, view);
            var list = new Listing_Standard { maxOneColumn = true };
            list.Begin(new Rect(0f, 0f, view.width, 100000f));
            try { body(list); }
            finally
            {
                s.height = list.CurHeight + 12f;
                list.End();
                Widgets.EndScrollView();
                Text.Font = GameFont.Small; GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        static void Note(Listing_Standard list, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            list.Label(text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        static void Header(Listing_Standard list, string text)
        {
            Text.Font = GameFont.Medium;
            list.Label(text);
            Text.Font = GameFont.Small;
            list.Gap(2f);
        }

        static void ReadOnlyBanner(Listing_Standard list)
        {
            if (!SmdLibraryStore.ReadOnly) return;
            GUI.color = ColorLibrary.RedReadable;
            list.Label("WBD_LibraryReadOnly".Translate());
            GUI.color = Color.white;
            list.Gap(6f);
        }

        /// <summary>Button row: two buttons side by side, each at least as wide as its label.</summary>
        static void TwoButtons(Listing_Standard list, string a, Action onA, string b, Action onB)
        {
            var row = list.GetRect(32f);
            float w = (row.width - 10f) / 2f;
            if (Widgets.ButtonText(new Rect(row.x, row.y + 2f, w, 28f), a)) onA?.Invoke();
            if (b != null && Widgets.ButtonText(new Rect(row.x + w + 10f, row.y + 2f, w, 28f), b)) onB?.Invoke();
            list.Gap(4f);
        }

        /// <summary>One preset row: default radio, name (with missing-mod note), Rename and Delete.</summary>
        static void PresetRow(Listing_Standard list, string name, bool isDefault, string missingSummary, string missingTip,
            Action setDefault, Action rename, Action delete)
        {
            var r = list.GetRect(30f);
            Widgets.DrawHighlightIfMouseover(r);
            if (Widgets.RadioButton(r.x + 8f, r.y + 3f, isDefault) && !isDefault) setDefault();
            TooltipHandler.TipRegion(new Rect(r.x, r.y, 36f, r.height), "WBD_SetDefault".Translate());
            string renameL = "WBD_Rename".Translate(), deleteL = "WBD_Delete".Translate();
            float bw = Mathf.Max(80f, Mathf.Max(Text.CalcSize(renameL).x, Text.CalcSize(deleteL).x) + 16f);
            var nameRect = new Rect(r.x + 40f, r.y + 4f, r.width - 40f - 2f * bw - 12f, 24f);
            string shown = isDefault ? "WBD_PresetIsDefault".Translate(name).ToString() : name;
            if (!string.IsNullOrEmpty(missingSummary))
            {
                shown += "  [" + missingSummary + "]";
                if (!string.IsNullOrEmpty(missingTip)) TooltipHandler.TipRegion(nameRect, missingTip);
            }
            Widgets.Label(nameRect, shown.Truncate(nameRect.width));
            if (Widgets.ButtonText(new Rect(r.xMax - 2f * bw - 6f, r.y + 2f, bw, 26f), renameL)) rename();
            if (Widgets.ButtonText(new Rect(r.xMax - bw, r.y + 2f, bw, 26f), deleteL)) delete();
        }

        // ------------------------------------------------------------------------------------------------ building presets
        public static void DrawBuildingPresets(Rect area) => Scrolled(area, sBuildings, list =>
        {
            Header(list, "WBD_PageBuildingPresets".Translate());
            Note(list, "WBD_PageBuildingPresetsHelp".Translate());
            ReadOnlyBanner(list);
            var rows = DefaultBillsDatabase.AllSavedDefNames()
                .Select(dn => new { defName = dn, label = DefaultBillsDatabase.KeyLabel(dn), presets = DefaultBillsDatabase.GetStored(dn) })
                .Where(r => r.presets != null && !r.presets.IsEmpty)
                .OrderBy(r => r.label)
                .ToList();
            if (rows.Count == 0)
            {
                list.Label("WBD_OverviewEmpty".Translate());
                return;
            }
            foreach (var row in rows)
            {
                list.Gap(6f);
                var header = list.GetRect(30f);
                string delAll = "WBD_DeleteAll".Translate();
                float dw = Mathf.Max(100f, Text.CalcSize(delAll).x + 16f);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(header.x, header.y + 4f, header.width - dw - 8f, 24f), row.label.Truncate(header.width - dw - 8f));
                if (Widgets.ButtonText(new Rect(header.xMax - dw, header.y + 2f, dw, 26f), delAll))
                {
                    string key = row.defName;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("WBD_DeleteAllConfirm".Translate(row.label), () => DefaultBillsDatabase.ClearAll(key), destructive: true));
                }
                Widgets.DrawLineHorizontal(header.x, header.yMax, header.width);
                foreach (var preset in row.presets.presets.ToList())
                {
                    string name = preset.name, key = row.defName;
                    bool isDefault = row.presets.DefaultPreset?.name == name;
                    var missing = SmdDefRefs.Missing(preset);
                    PresetRow(list, name, isDefault,
                        missing.IsEmpty ? null : missing.Summary(),
                        missing.IsEmpty ? null : missing.Summary() + "\n" + string.Join("\n", missing.unknownDefs.Take(20)),
                        () => DefaultBillsDatabase.SetDefaultPreset(key, name),
                        () => PromptRename(name, n => DefaultBillsDatabase.RenamePreset(key, name, n)),
                        () => DefaultBillsDatabase.RemovePreset(key, name));
                }
            }
        });

        static void PromptRename(string oldName, Func<string, bool> rename)
        {
            Find.WindowStack.Add(new WBD_Dialog_NamePreset("WBD_RenameTitle".Translate(oldName), oldName, newName =>
            {
                if (!rename(newName))
                    Messages.Message("WBD_RenameFailed".Translate(newName), MessageTypeDefOf.RejectInput, false);
            }));
        }

        // ------------------------------------------------------------------------------------------------ colonist presets
        public static void DrawColonistPresets(Rect area) => Scrolled(area, sColonists, list =>
        {
            Header(list, "WBD_PageColonistPresets".Translate());
            Note(list, "WBD_PageColonistPresetsHelp".Translate());
            if (FreeWillCompat.Active) Note(list, "WBD_PawnFreeWillNote".Translate());
            ReadOnlyBanner(list);
            var presets = PawnDefaultsStore.GetStored();
            if (presets == null || presets.IsEmpty)
            {
                list.Label("WBD_PawnOverviewEmpty".Translate());
                return;
            }
            foreach (var preset in presets.presets.ToList())
            {
                string name = preset.name;
                PresetRow(list, name, presets.DefaultPreset?.name == name, null, null,
                    () => PawnDefaultsStore.SetDefaultPreset(name),
                    () => PromptRename(name, n => PawnDefaultsStore.RenamePreset(name, n)),
                    () => PawnDefaultsStore.RemovePreset(name));
            }
        });

        // ------------------------------------------------------------------------------------------------ policy set
        public static void DrawPolicies(Rect area) => Scrolled(area, sPolicies, list =>
        {
            Header(list, "WBD_PolicyDefaultsHeader".Translate());
            Note(list, "WBD_PagePoliciesHelp".Translate());
            ReadOnlyBanner(list);
            if (!PolicyDefaultsController.GameReady)
                Note(list, "WBD_PolicyNoGame".Translate());
            else
                TwoButtons(list, "WBD_SaveDefaultPolicies".Translate(), () =>
                {
                    if (PolicyDefaultsStore.SaveCurrentColony())
                        Messages.Message("WBD_PolicyCrossSaveSaved".Translate(), MessageTypeDefOf.TaskCompletion, false);
                    else
                        Messages.Message(DefaultBillsController.SaveFailedText(), MessageTypeDefOf.RejectInput, false);
                }, PolicyDefaultsStore.HasSet() ? "WBD_ApplyDefaultPolicies".Translate().ToString() : null, () =>
                {
                    if (PolicyDefaultsStore.ApplyToCurrentColony())
                        Messages.Message("WBD_PolicyCrossSaveApplied".Translate(), MessageTypeDefOf.TaskCompletion, false);
                    else
                        Messages.Message(DefaultBillsController.SaveFailedText(), MessageTypeDefOf.RejectInput, false);
                });

            var savedSet = PolicyDefaultsStore.GetSet();
            list.Gap(6f);
            if (savedSet == null || savedSet.IsEmpty)
                Note(list, "WBD_PolicyNoneSaved".Translate());
            else
            {
                list.Label("WBD_PolicySavedSetHeader".Translate());
                foreach (var kind in PolicyDefaultsController.AllKinds)
                {
                    var saved = savedSet.ListFor(kind);
                    if (saved == null || saved.Count == 0) continue;
                    var def = savedSet.DefaultLabelFor(kind);
                    var labels = saved.Select(t => t.label == def ? "★ " + t.label : t.label).ToList();
                    list.Label("   " + PolicyDefaultsController.KindLabel(kind) + ": " + string.Join(", ", labels));
                }
                var clear = list.GetRect(30f);
                string cl = "WBD_PolicyClearSaved".Translate();
                if (Widgets.ButtonText(new Rect(clear.x, clear.y + 2f, Mathf.Max(180f, Text.CalcSize(cl).x + 20f), 26f), cl))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("WBD_PolicyClearConfirm".Translate(), PolicyDefaultsStore.ClearAll, destructive: true));
            }

            list.GapLine();
            list.Label("WBD_PolicyColonyDefaultsHeader".Translate());
            if (!PolicyDefaultsController.GameReady)
            {
                Note(list, "WBD_PolicyNoGame".Translate());
                return;
            }
            foreach (var kind in PolicyDefaultsController.AllKinds)
            {
                var header = list.GetRect(30f);
                string manage = "WBD_PolicyManage".Translate();
                float mw = Mathf.Max(124f, Text.CalcSize(manage).x + 16f);
                Widgets.Label(new Rect(header.x, header.y + 4f, header.width - mw - 8f, 24f), PolicyDefaultsController.KindLabel(kind));
                if (Widgets.ButtonText(new Rect(header.xMax - mw, header.y + 2f, mw, 26f), manage))
                    PolicyDefaultsController.OpenManageDialog(kind);
                Widgets.DrawLineHorizontal(header.x, header.yMax, header.width);
                var currentDefault = PolicyDefaultsController.DefaultPolicy(kind);
                foreach (var live in PolicyDefaultsController.LivePolicies(kind).ToList())
                {
                    var name = PolicyDefaultsController.PolicyLabel(live) ?? "?";
                    bool isDefault = ReferenceEquals(live, currentDefault);
                    var r = list.GetRect(28f);
                    if (Widgets.RadioButton(r.x + 12f, r.y + 2f, isDefault) && !isDefault
                        && PolicyDefaultsController.SetColonyDefault(kind, live))
                        Messages.Message("WBD_PolicyDefaultSet".Translate(name), MessageTypeDefOf.TaskCompletion, false);
                    var nameRect = new Rect(r.x + 46f, r.y + 4f, r.width - 46f, 24f);
                    Widgets.Label(nameRect, (isDefault ? "WBD_PresetIsDefault".Translate(name).ToString() : name).Truncate(nameRect.width));
                }
                list.Gap(8f);
            }
        });

        // ------------------------------------------------------------------------------------------------ colony settings
        public static void DrawColony(Rect area) => Scrolled(area, sColony, list =>
        {
            Header(list, "WBD_ColonyHeader".Translate());
            Note(list, "WBD_PageColonyHelp".Translate());
            ReadOnlyBanner(list);
            if (PolicyDefaultsController.GameReady)
                TwoButtons(list, "WBD_ColonySave".Translate(), () =>
                {
                    if (ColonySettingsStore.SaveCurrentGame())
                        Messages.Message("WBD_ColonySaved".Translate(), MessageTypeDefOf.TaskCompletion, false);
                    else
                        Messages.Message(DefaultBillsController.SaveFailedText(), MessageTypeDefOf.RejectInput, false);
                }, ColonySettingsStore.Has() ? "WBD_ColonyApply".Translate().ToString() : null, () =>
                {
                    if (ColonySettingsStore.ApplyToCurrentGame())
                        Messages.Message("WBD_ColonyApplied".Translate(), MessageTypeDefOf.TaskCompletion, false);
                });
            else
                Note(list, "WBD_PolicyNoGame".Translate());
            list.Gap(6f);
            if (!ColonySettingsStore.Has())
            {
                Note(list, "WBD_ColonyNoneSaved".Translate());
                return;
            }
            foreach (var line in ColonySettingsStore.SummaryLines())
                list.Label("   " + line);
            var clr = list.GetRect(30f);
            string cl = "WBD_ColonyClear".Translate();
            if (Widgets.ButtonText(new Rect(clr.x, clr.y + 2f, Mathf.Max(200f, Text.CalcSize(cl).x + 20f), 26f), cl))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("WBD_ColonyClearConfirm".Translate(), ColonySettingsStore.Clear, destructive: true));
        });

        // ------------------------------------------------------------------------------------------------ library (status; profiles come next)
        public static void DrawLibrary(Rect area)
        {
            if (LibraryPage != null) { LibraryPage(area); return; }
            Scrolled(area, sLibrary, list =>
            {
                Header(list, "WBD_PageLibrary".Translate());
                Note(list, "WBD_PageLibraryHelp".Translate());
                var s = SmdLibraryApi.Status();
                if (s.readOnly)
                {
                    GUI.color = ColorLibrary.RedReadable;
                    list.Label("WBD_LibraryReadOnly".Translate());
                    GUI.color = Color.white;
                }
                list.Label("WBD_LibCounts".Translate(s.buildingPresets, s.buildingSets, s.zonePresets, s.pawnPresets));
                list.Label((s.hasPolicies ? "WBD_LibPoliciesYes" : "WBD_LibPoliciesNo").Translate());
                list.Label((s.hasColony ? "WBD_LibColonyYes" : "WBD_LibColonyNo").Translate());
                list.Label("WBD_LibProfiles".Translate(s.profiles));
                var miss = SmdLibraryApi.MissingInLibrary();
                if (!miss.IsEmpty) list.Label("WBD_LibMissing".Translate(miss.Summary()));
                if (s.lastMigration != null) Note(list, "WBD_LibMigrated".Translate());
                list.Gap(8f);
                var row = list.GetRect(30f);
                string open = "WBD_LibOpenFolder".Translate(), again = "WBD_LibReimport".Translate();
                float w1 = Mathf.Max(180f, Text.CalcSize(open).x + 20f), w2 = Mathf.Max(220f, Text.CalcSize(again).x + 20f);
                if (Widgets.ButtonText(new Rect(row.x, row.y + 2f, w1, 26f), open))
                {
                    try { System.IO.Directory.CreateDirectory(SmdPaths.LibraryFolder); Application.OpenURL("file://" + SmdPaths.LibraryFolder); }
                    catch (Exception e) { Messages.Message(e.Message, MessageTypeDefOf.RejectInput, false); }
                }
                if (Widgets.ButtonText(new Rect(row.x + w1 + 10f, row.y + 2f, w2, 26f), again))
                    Messages.Message(SmdLibraryApi.ReimportLegacy(false), MessageTypeDefOf.NeutralEvent, false);
                TooltipHandler.TipRegion(new Rect(row.x + w1 + 10f, row.y + 2f, w2, 26f), "WBD_LibReimportTip".Translate());
            });
        }
    }
}
