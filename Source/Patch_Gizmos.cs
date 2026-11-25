using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Adds a single "Bill defaults" command button to a selected player workbench.
    /// This is layout-independent (unlike the in-tab button) and therefore the robust,
    /// always-available way to reach the save/apply/clear actions.
    /// </summary>
    [HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    [StaticConstructorOnStartup]   // the icon field: RimWorld warns at startup without it (the texture is still loaded lazily)
    public static class Patch_Building_GetGizmos
    {
        private static Texture2D _icon;

        private static Texture2D Icon
        {
            get
            {
                if (_icon == null)
                    _icon = ContentFinder<Texture2D>.Get("UI/WBD_Defaults", false) ?? BaseContent.BadTex;
                return _icon;
            }
        }

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building __instance)
        {
            foreach (var gizmo in __result)
                yield return gizmo;

            var settings = WBD_Mod.Settings;
            if (settings == null || !settings.showGizmo || !SmdSections.On(SmdSections.Buildings))
                yield break;

            // Any building we can capture a config from (bills, storage, or a PRF splitter).
            if (!DefaultBillsController.Capturable(__instance))
                yield break;

            // Only for the player's own buildings.
            if (__instance.Faction != Faction.OfPlayerSilentFail)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "WBD_Defaults".Translate(),
                defaultDesc = "WBD_DefaultsTip".Translate(),
                icon = Icon,
                action = () => DefaultBillsController.OpenDefaultsMenu(__instance)
            };
        }
    }
}
