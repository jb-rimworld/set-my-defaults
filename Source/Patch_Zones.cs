using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>The "Defaults" gizmo on stockpile zones (save, apply to all selected zones, set default).</summary>
    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.GetGizmos))]
    public static class Patch_ZoneStockpile_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Zone_Stockpile __instance)
        {
            foreach (var g in __result)
                yield return g;
            var gizmo = ZoneGizmo.Make(__instance);
            if (gizmo != null)
                yield return gizmo;
        }
    }

    /// <summary>The "Defaults" gizmo on growing zones.</summary>
    [HarmonyPatch(typeof(Zone_Growing), nameof(Zone_Growing.GetGizmos))]
    public static class Patch_ZoneGrowing_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Zone_Growing __instance)
        {
            foreach (var g in __result)
                yield return g;
            var gizmo = ZoneGizmo.Make(__instance);
            if (gizmo != null)
                yield return gizmo;
        }
    }

    internal static class ZoneGizmo
    {
        private static Texture2D _icon;

        public static Gizmo Make(Zone zone)
        {
            var s = WBD_Mod.Settings;
            if (s == null || !s.showGizmo || !SmdSections.On(SmdSections.Zones) || zone == null)
                return null;
            if (_icon == null)
                _icon = ContentFinder<Texture2D>.Get("UI/WBD_Defaults", false) ?? BaseContent.BadTex;
            return new Command_Action
            {
                defaultLabel = "WBD_Defaults".Translate(),
                defaultDesc = "WBD_ZoneDefaultsTip".Translate(),
                icon = _icon,
                action = () => DefaultBillsController.OpenZoneMenu(zone)
            };
        }
    }

    /// <summary>
    /// A new stockpile (or dumping) zone drawn by the player starts with its saved default filter (M9). Hooked on the
    /// designator, not on the zone constructor, so only zones the player draws are touched (not quest/ritual zones).
    /// </summary>
    [HarmonyPatch(typeof(Designator_ZoneAddStockpile), "MakeNewZone")]
    public static class Patch_DesignatorStockpile_MakeNewZone
    {
        private static readonly AccessTools.FieldRef<Designator_ZoneAddStockpile, StorageSettingsPreset> PresetRef =
            AccessTools.FieldRefAccess<Designator_ZoneAddStockpile, StorageSettingsPreset>("preset");

        public static void Postfix(Designator_ZoneAddStockpile __instance, Zone __result)
        {
            try
            {
                var s = WBD_Mod.Settings;
                if (s == null || !s.zoneAutoApplyOnCreate || !SmdSections.On(SmdSections.Zones) || !(__result is Zone_Stockpile zone))
                    return;
                string key;
                switch (PresetRef(__instance))
                {
                    case StorageSettingsPreset.DefaultStockpile: key = DefaultBillsDatabase.ZoneStockpile; break;
                    case StorageSettingsPreset.DumpingStockpile: key = DefaultBillsDatabase.ZoneDumping; break;
                    default: return;
                }
                DefaultBillsDatabase.GetOwnDefaultConfig(key)?.ApplyToZone(zone, zoneHasCells: false);
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not apply the stockpile zone default: " + e.Message);
            }
        }
    }

    /// <summary>A new growing zone starts with its saved default plant and sow/cut toggles (M10).</summary>
    [HarmonyPatch(typeof(Designator_ZoneAdd_Growing), "MakeNewZone")]
    public static class Patch_DesignatorGrowing_MakeNewZone
    {
        public static void Postfix(Zone __result)
        {
            try
            {
                var s = WBD_Mod.Settings;
                if (s == null || !s.growingAutoApplyOnCreate || !SmdSections.On(SmdSections.Zones) || !(__result is Zone_Growing zone))
                    return;
                DefaultBillsDatabase.GetOwnDefaultConfig(DefaultBillsDatabase.ZoneGrowing)?.ApplyToZone(zone, zoneHasCells: false);
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not apply the growing zone default: " + e.Message);
            }
        }
    }
}
