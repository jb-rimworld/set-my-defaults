using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Adds a "Pawn defaults" command button to a selected player colonist, mirroring the building
    /// gizmo. Opens the same save/apply/manage menu; with several colonists selected, applying acts
    /// on all of them. Layout-independent passthrough (yields existing gizmos unchanged).
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
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

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var gizmo in __result)
                yield return gizmo;

            var settings = WBD_Mod.Settings;
            if (settings == null || !settings.pawnShowGizmo)
                yield break;
            if (!PawnDefaultsController.IsTarget(__instance))
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "WBD_PawnDefaults".Translate(),
                defaultDesc = "WBD_PawnDefaultsTip".Translate(),
                icon = Icon,
                action = () => PawnDefaultsController.OpenMenu(__instance)
            };
        }
    }
}
