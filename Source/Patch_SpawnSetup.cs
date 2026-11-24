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
        /// Shared apply logic. Blueprint-placed config wins; otherwise the building's default (own, else bench family):
        ///  - bills to a fresh bill giver (empty stack), as in 1.x;
        ///  - storage filter and plant ONLY on new construction (inside Frame.CompleteConstruction), never on a
        ///    reinstall, a gravship landing or a load, so a shelf you moved keeps its settings (M9/M10).
        /// Storage buildings that join storage groups get their default through their blueprint instead
        /// (<see cref="Patch_BlueprintStorage_PostMake"/>), because vanilla copies the blueprint's settings over the
        /// finished building right after it spawns.
        /// </summary>
        internal static void OnSpawn(Building building, Map map)
        {
            try
            {
                var pending = WBD_PendingConfigs.For(map);
                if (pending != null && pending.NotifySpawned(building))
                    return;

                var settings = WBD_Mod.Settings;
                if (settings == null || !SmdSections.On(SmdSections.Buildings))
                    return;

                var parts = ConfigParts.None;
                if (settings.autoApplyOnPlace && building is IBillGiver billGiver && billGiver.BillStack != null && billGiver.BillStack.Count == 0)
                    parts |= ConfigParts.Bills;
                bool newBuild = Patch_Frame_CompleteConstruction.Running;
                if (newBuild && settings.storageAutoApplyOnBuild && building is IStoreSettingsParent && !(building is IStorageGroupMember))
                    parts |= ConfigParts.Storage;
                if (newBuild && settings.plantAutoApplyOnBuild && building is IPlantToGrowSettable)
                    parts |= ConfigParts.Plant;
                if (parts == ConfigParts.None)
                    return;

                var config = DefaultBillsDatabase.GetDefaultConfig(building.def);
                if (config == null || config.IsEmpty)
                    return;
                config.ApplyTo(building, replaceBills: false, parts);
            }
            catch (Exception e)
            {
                WBD_Log.Error("Error while applying defaults to " + building.def?.defName + ": " + e);
            }
        }
    }
}
