using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Applies configuration the moment a building is built/placed:
    ///  1. a blueprint-placed per-building config (bills + storage + splitter) takes precedence;
    ///  2. otherwise, the per-workbench-type default BILLS are applied to a fresh bill giver.
    /// Patched on Verse.Building.SpawnSetup so it covers vanilla workbenches, PRF assemblers,
    /// shelves/storage and conveyor splitters — every relevant building goes through it.
    /// </summary>
    public static class Patch_SpawnSetup
    {
        public static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(Building), "SpawnSetup", new[] { typeof(Map), typeof(bool) });
            if (target == null)
            {
                WBD_Log.Error("Could not locate Building.SpawnSetup; auto-apply of defaults is disabled.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_SpawnSetup), nameof(Postfix)));
        }

        public static void Postfix(Building __instance, Map map, bool respawningAfterLoad)
        {
            if (__instance == null || respawningAfterLoad)
                return;
            // Cheap gate first: skip everything that can't hold a config (walls, etc.).
            if (!DefaultBillsController.Capturable(__instance))
                return;
            if (__instance.Faction != Faction.OfPlayerSilentFail)
                return;
            // PRF dynamic-recipe assemblers inject their recipes AFTER base.SpawnSetup, so defer
            // them to PrfCompat which runs once those recipes exist in def.AllRecipes.
            if (PrfCompat.IsDeferredBillGiver(__instance))
                return;
            OnSpawn(__instance, map);
        }

        /// <summary>
        /// Shared apply logic. Blueprint-placed config wins; otherwise per-type default bills
        /// are applied to a genuinely fresh bill giver (empty stack). Storage/splitter are NOT
        /// auto-applied here (only via blueprint or manual apply) to avoid clobbering re-spawns.
        /// </summary>
        internal static void OnSpawn(Building building, Map map)
        {
            try
            {
                var pending = WBD_PendingConfigs.For(map);
                if (pending != null && pending.NotifySpawned(building))
                    return;

                var settings = WBD_Mod.Settings;
                if (settings == null || !settings.autoApplyOnPlace)
                    return;
                if (!(building is IBillGiver billGiver))
                    return;
                var stack = billGiver.BillStack;
                if (stack == null || stack.Count > 0)
                    return;

                var config = DefaultBillsDatabase.GetDefaultConfig(building.def);
                if (config == null || config.bills == null || config.bills.Count == 0)
                    return;
                config.ApplyTo(building, replaceBills: false, includeNonBills: false);
            }
            catch (Exception e)
            {
                WBD_Log.Error("Error while applying defaults to " + building.def?.defName + ": " + e);
            }
        }
    }
}
