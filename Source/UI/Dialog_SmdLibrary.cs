using System.Collections.Generic;
using JBFramework;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The in-game library window, opened from the Defaults button of buildings, zones and colonists (and the Bills tab): saved
    /// defaults, profiles and import/export in one place, without going through Mod settings. Draws the same pages as the settings
    /// window. Does not pause the game, so the player can keep watching the colony.
    /// </summary>
    public sealed class Dialog_SmdLibrary : Window
    {
        public string Tab;
        readonly List<TabRecord> tabs = new List<TabRecord>();

        /// <summary>The design size 980x700, scaled like every JB window and kept inside the screen.</summary>
        public override Vector2 InitialSize => KitUI.Size(980f, 700f, 720f, 460f, 0.94f, 0.92f);

        public Dialog_SmdLibrary(string tab)
        {
            Tab = ValidTab(tab);
            doCloseX = true; doCloseButton = false; closeOnClickedOutside = false; absorbInputAroundWindow = false;
            forcePause = false; draggable = true; resizeable = true; preventCameraMotion = false;
        }

        static bool ProfilesOn => SmdSections.On(SmdSections.Library);

        static string ValidTab(string t)
        {
            if (t == SmdLibraryUi.PageProfiles || t == SmdLibraryUi.PageShare) return ProfilesOn ? t : SmdLibraryUi.PageLibrary;
            return SmdLibraryUi.PageLibrary;
        }

        /// <summary>Opens the window (one at a time) on a tab; <paramref name="jumpTo"/> scrolls the saved-defaults list to an area.</summary>
        public static void Open(string tab, string jumpTo = null)
        {
            SmdLibraryUi.JumpTo = jumpTo;
            var open = Find.WindowStack.WindowOfType<Dialog_SmdLibrary>();
            if (open != null) { open.Tab = ValidTab(tab); return; }
            Find.WindowStack.Add(new Dialog_SmdLibrary(tab));
        }

        public override void DoWindowContents(Rect inRect)
        {
            SmdUi.Probe("window", () => Draw(inRect));
        }

        void Draw(Rect inRect)
        {
            Tab = ValidTab(Tab);
            // header: mark + title
            float y = inRect.y;
            var mark = ContentFinder<Texture2D>.Get("UI/WBD_Defaults", false);
            float tx = inRect.x;
            if (mark != null) { GUI.DrawTexture(new Rect(inRect.x, y, 36f, 36f), mark); tx += 46f; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(tx, y + 2f, inRect.width - tx - 40f, 34f), "WBD_LibWindowTitle".Translate());
            Text.Font = GameFont.Small;
            y += 44f;

            // tabs
            tabs.Clear();
            void AddTab(string id, string label) => tabs.Add(new TabRecord(label, () => Tab = id, Tab == id));
            AddTab(SmdLibraryUi.PageLibrary, "WBD_TabLibrary".Translate());
            if (ProfilesOn)
            {
                AddTab(SmdLibraryUi.PageProfiles, "WBD_TabProfiles".Translate());
                AddTab(SmdLibraryUi.PageShare, "WBD_TabShare".Translate());
            }
            y += 32f; // TabDrawer draws above its base rect
            var body = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - 42f);
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs, 1, 220f);
            var inner = body.ContractedBy(12f);
            switch (Tab)
            {
                case SmdLibraryUi.PageProfiles: SmdLibraryUi.DrawProfiles(inner); break;
                case SmdLibraryUi.PageShare: SmdLibraryUi.DrawShare(inner); break;
                default: SmdLibraryUi.DrawLibrary(inner); break;
            }

            // footer
            var fr = new Rect(inRect.x, inRect.yMax - 34f, inRect.width, 34f);
            string close = "CloseButton".Translate(), all = "WBD_LibAllSettings".Translate();
            float cw = SmdUi.BtnW(close, 140f), aw = SmdUi.BtnW(all, 160f);
            if (Widgets.ButtonText(new Rect(fr.xMax - cw, fr.y + 4f, cw, SmdUi.BtnH), close)) Close();
            if (SmdUi.Button(fr.xMax - cw - aw - 10f, fr.y + 4f, all, out _, true, "WBD_LibAllSettingsTip".Translate(), 160f))
            {
                Close();
                WBD_Mod.OpenSettings("overview");
            }
        }
    }
}
