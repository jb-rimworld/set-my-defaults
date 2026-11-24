using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Optional Project RimFactory support. PRF's dynamic-recipe assemblers
    /// (Building_DynamicBillGiver and subclasses) inject their runnable recipes into
    /// def.AllRecipes AFTER base.SpawnSetup returns. Our generic Building.SpawnSetup postfix
    /// therefore runs too early for them, so we defer those buildings and re-apply here, once
    /// the recipes are present. Resolved purely by reflection: no hard dependency on PRF.
    /// </summary>
    public static class PrfCompat
    {
        private static bool _resolved;
        private static bool _patchApplied;
        private static Type _dynamicBillGiver;

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                _dynamicBillGiver = GenTypes.GetTypeInAnyAssembly(
                    "ProjectRimFactory.SAL3.Things.Assemblers.Building_DynamicBillGiver");
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not detect Project RimFactory: " + e.Message);
            }
        }

        /// <summary>
        /// True only if this is a PRF dynamic-recipe assembler AND our deferred patch is live,
        /// so the generic SpawnSetup postfix should skip it and let us handle it later.
        /// If the patch failed to apply, we do NOT defer (the generic path still runs).
        /// </summary>
        public static bool IsDeferredBillGiver(Thing thing)
        {
            Resolve();
            return _patchApplied && _dynamicBillGiver != null && thing != null
                   && _dynamicBillGiver.IsInstanceOfType(thing);
        }

        public static void Apply(Harmony harmony)
        {
            Resolve();
            if (_dynamicBillGiver == null)
                return; // PRF not installed

            try
            {
                var target = AccessTools.Method(_dynamicBillGiver, "SpawnSetup", new[] { typeof(Map), typeof(bool) });
                if (target == null)
                {
                    WBD_Log.Warn("PRF Building_DynamicBillGiver.SpawnSetup not found; assembler defaults use the generic path.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(PrfCompat), nameof(Postfix)));
                _patchApplied = true;
                WBD_Log.Msg("Project RimFactory assembler support enabled.");
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to patch PRF assembler SpawnSetup: " + e);
            }
        }

        // Runs after the assembler has injected its dynamic recipes into def.AllRecipes.
        public static void Postfix(Building __instance, Map map, bool respawningAfterLoad)
        {
            if (__instance == null || respawningAfterLoad)
                return;
            if (__instance.Faction != Faction.OfPlayerSilentFail)
                return;
            Patch_SpawnSetup.OnSpawn(__instance, map);
        }
    }
}
