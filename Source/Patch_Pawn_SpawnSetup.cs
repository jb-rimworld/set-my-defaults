using HarmonyLib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Applies the default pawn preset when a pawn is placed on a map and is NOT being re-spawned
    /// after a save load. Catches starting colonists, map-gen, and pod/gravship/cryptosleep arrivals
    /// of pawns that already carry the player faction (those never hit SetFaction while "joining").
    /// All once-only logic lives in <see cref="NewColonistDefaults"/> + the GameComponent marker.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_Pawn_SpawnSetup
    {
        public static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad)
                return;
            NewColonistDefaults.TryApply(__instance, fromHook: true);
        }
    }
}
