using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Draws a small "Defaults" button into the Bills tab header, in the empty gap
    /// between the vanilla "Add bill" button (left) and the copy/paste cluster (right).
    /// It opens the same save/apply/clear menu as the gizmo. Purely additive: it reads
    /// the vanilla window size and draws on top in a Postfix, so it never disturbs the
    /// existing layout or other mods that decorate this tab.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Bills), "FillTab")]
    [StaticConstructorOnStartup]   // the icon field: RimWorld warns at startup without it (the texture is still loaded lazily)
    public static class Patch_ITab_Bills_FillTab
    {
        private static readonly FieldInfo WinSizeField =
            AccessTools.Field(typeof(ITab_Bills), "WinSize");

        private static bool _winSizeRead;
        private static Vector2 _winSize;

        private static Vector2 WinSize
        {
            get
            {
                if (!_winSizeRead)
                {
                    _winSizeRead = true;
                    if (WinSizeField != null && WinSizeField.FieldType == typeof(Vector2))
                        _winSize = (Vector2)WinSizeField.GetValue(null);
                }
                return _winSize;
            }
        }

        // Nice Bill Tab (Andromeda.NiceBillTab) replaces the whole Bills tab with its own layout and its own window
        // size, so the vanilla-gap button would land on top of its search bar. In that case we draw a compact
        // icon next to its top-right icons instead (and read the real window size from the tab instance).
        private static readonly FieldInfo TabSizeField =
            AccessTools.Field(typeof(InspectTabBase), "size");
        private static bool _nbtResolved;
        private static System.Func<bool> _nbtEnabled;
        private static System.Func<int> _nbtSelectionCount;
        private static Texture2D _icon;

        private static void ResolveNiceBillTab()
        {
            if (_nbtResolved) return;
            _nbtResolved = true;
            try
            {
                var settings = AccessTools.TypeByName("NiceBillTab.Settings");
                var drawer = AccessTools.TypeByName("NiceBillTab.TabBillsDrawer");
                if (settings == null || drawer == null) return;

                var enabled = AccessTools.Property(settings, "EnabledMod");
                var enabledField = AccessTools.Field(settings, "EnabledMod");
                if (enabled != null) _nbtEnabled = () => (bool)enabled.GetValue(null);
                else if (enabledField != null) _nbtEnabled = () => (bool)enabledField.GetValue(null);

                var sel = AccessTools.Property(drawer, "Selections");
                var selField = AccessTools.Field(drawer, "Selections");
                _nbtSelectionCount = () =>
                {
                    object list = sel != null ? sel.GetValue(null) : selField?.GetValue(null);
                    return list is System.Collections.ICollection c ? c.Count : 0;
                };
            }
            catch (System.Exception e)
            {
                WBD_Log.Warn("Nice Bill Tab compat could not be set up: " + e.Message);
                _nbtEnabled = null;
            }
        }

        public static void Postfix(ITab_Bills __instance)
        {
            var settings = WBD_Mod.Settings;
            if (settings == null || !settings.showTabButton || !SmdSections.On(SmdSections.Buildings))
                return;

            var selected = Find.Selector.SingleSelectedThing;
            if (selected == null || !(selected is IBillGiver))
                return;
            if (selected.Faction != Faction.OfPlayerSilentFail)
                return;

            ResolveNiceBillTab();
            if (_nbtEnabled != null && _nbtEnabled() && TabSizeField != null)
            {
                DrawForNiceBillTab(__instance, selected);
                return;
            }

            var winSize = WinSize;
            if (winSize.x <= 0f)
                return; // could not read the layout; the gizmo still works

            var content = new Rect(0f, 0f, winSize.x, winSize.y).ContractedBy(10f);
            if (content.width < 330f)
                return; // not enough horizontal room; fall back to the gizmo

            var buttonRect = new Rect(content.x + 158f, content.y, 124f, 29f);
            if (Widgets.ButtonText(buttonRect, "WBD_Defaults".Translate()))
                DefaultBillsController.OpenDefaultsMenu(selected);
            TooltipHandler.TipRegion(buttonRect, "WBD_DefaultsTip".Translate());
        }

        private static void DrawForNiceBillTab(ITab_Bills tab, Thing selected)
        {
            if (!(TabSizeField.GetValue(tab) is Vector2 size) || size.x <= 0f)
                return;
            int selections = _nbtSelectionCount != null ? _nbtSelectionCount() : 0;
            // Nice Bill Tab's own icons (24 px each, right-aligned): toggle at -48 (-72 with open side panels), settings
            // gear at -48 (only with open side panels), DEV button at -96. The first free slot is -72 without side
            // panels, otherwise -96 (or -120 in dev mode, where the DEV button sits at -96).
            float slots = selections > 0 ? (Prefs.DevMode ? 5f : 4f) : 3f;
            var iconRect = new Rect(size.x - 24f * slots, 0f, 24f, 24f);
            if (_icon == null)
                _icon = ContentFinder<Texture2D>.Get("UI/WBD_Defaults", false) ?? BaseContent.BadTex;
            var inner = iconRect.ContractedBy(2f);
            GUI.color = Mouse.IsOver(iconRect) ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            GUI.DrawTexture(inner, _icon);
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(iconRect))
                DefaultBillsController.OpenDefaultsMenu(selected);
            TooltipHandler.TipRegion(iconRect, "WBD_DefaultsTip".Translate());
        }
    }
}
