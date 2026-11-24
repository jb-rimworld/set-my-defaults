using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Marks "a frame is being completed right now", so the spawn hook can tell new construction apart from a
    /// reinstall, a gravship landing or a load (all of which also run SpawnSetup). Prefix + finalizer, so the flag
    /// can never stay set after an exception.
    /// </summary>
    [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
    public static class Patch_Frame_CompleteConstruction
    {
        [ThreadStatic] private static int _depth;

        public static bool Running => _depth > 0;

        public static void Prefix() => _depth++;

        public static Exception Finalizer(Exception __exception)
        {
            if (_depth > 0)
                _depth--;
            return __exception;
        }
    }

    /// <summary>
    /// Shelves and other storage buildings get their default filter on NEW construction (M9): the default is written
    /// into the storage blueprint when it is placed, vanilla then carries it blueprint -> frame -> building. The player
    /// can still change the blueprint's storage before it is built. Auto-rebuild and "frame back to blueprint" copy
    /// the old settings over this afterwards, as vanilla intends.
    /// </summary>
    [HarmonyPatch(typeof(Blueprint_Storage), nameof(Blueprint_Storage.PostMake))]
    public static class Patch_BlueprintStorage_PostMake
    {
        public static void Postfix(Blueprint_Storage __instance)
        {
            try
            {
                var settings = WBD_Mod.Settings;
                if (settings == null || !settings.storageAutoApplyOnBuild || !SmdSections.On(SmdSections.Buildings))
                    return;
                if (Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
                    return;
                var def = __instance?.def?.entityDefToBuild as ThingDef;
                if (def == null || __instance.settings == null)
                    return;
                var config = DefaultBillsDatabase.GetDefaultConfig(def);
                config?.storage?.ApplyTo(__instance.settings, def.building?.fixedStorageSettings?.filter);
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not apply the storage default to a blueprint: " + e.Message);
            }
        }
    }
}
