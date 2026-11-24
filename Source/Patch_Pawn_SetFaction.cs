using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Applies the default pawn preset the moment a pawn becomes a player-faction pawn: recruiting a
    /// prisoner, a wanderer/refugee/quest pawn joining, or a baby being born into the colony. These
    /// already-spawned pawns never get a fresh SpawnSetup, so SetFaction is the chokepoint that
    /// covers them. Filtered strictly to the player faction so leaving the colony never triggers it.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class Patch_Pawn_SetFaction
    {
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (newFaction == null || newFaction != Faction.OfPlayerSilentFail)
                return;
            NewColonistDefaults.TryApply(__instance, fromHook: true);
        }
    }
}
