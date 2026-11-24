using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The custom pages of the settings window (JB Framework draws sidebar, search, switches and plain settings; these pages show
    /// what is not a plain value: the policy set and the colony settings of this colony; the saved presets, profiles and share strings are in SmdLibraryUi). Every page scrolls
    /// itself. The framework runs each draw inside a UI fence.
    /// </summary>
    public static class SmdPages
    {
        // ------------------------------------------------------------------------------------------------ scrolling helper
        sealed class Scroll { public Vector2 pos; public float height = 400f; }
        static readonly Scroll sPolicies = new Scroll(), sColony = new Scroll();

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
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            list.Label(text);
            GUI.color = Color.white;
            list.Gap(4f);
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

        // ------------------------------------------------------------------------------------------------ policy set
        public static void DrawPolicies(Rect area) => Scrolled(area, sPolicies, list =>
        {
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
    }
}
