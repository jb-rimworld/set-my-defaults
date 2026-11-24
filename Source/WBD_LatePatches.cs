using System;
using HarmonyLib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Applies the optional cross-mod patches (Project RimFactory, Blueprints Forked) from a
    /// [StaticConstructorOnStartup] constructor, which runs after ALL mod assemblies are loaded.
    /// This makes the integrations independent of mod load order: the other mods' types resolve
    /// here regardless of whether they loaded before or after us.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WBD_LatePatches
    {
        static WBD_LatePatches()
        {
            var harmony = new Harmony("juliusberger.workbenchdefaults.compat");

            try { PrfCompat.Apply(harmony); }
            catch (Exception e) { WBD_Log.Error("Project RimFactory compatibility patch failed: " + e); }

            try { BlueprintsCompat.Apply(harmony); }
            catch (Exception e) { WBD_Log.Error("Blueprints Forked compatibility patch failed: " + e); }
        }
    }
}
