using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Optional, reflection-only coexistence with "Free Will" (freemapa.freewill) and its fork
    /// "Free-ish Will" (stoneman.freewill). Both ship the same assembly/namespace ("FreeWill") and
    /// expose <c>FreeWill.FreeWill_WorldComponent.HasFreeWill(Pawn, string)</c>, which is exactly the
    /// gate their own ticking uses to decide whether to (re)write a pawn's work priorities.
    ///
    /// Free Will continuously owns the work priorities of any pawn it manages, so a one-shot default
    /// applied at join would just be overwritten a few ticks later. We therefore skip the WORK axis
    /// for pawns Free Will manages (schedule/policies are untouched by Free Will and still apply).
    /// No hard dependency: everything no-ops cleanly when neither mod is present.
    /// </summary>
    public static class FreeWillCompat
    {
        private static bool _resolved;
        private static Type _worldCompType;
        private static MethodInfo _hasFreeWill;

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                _worldCompType = GenTypes.GetTypeInAnyAssembly("FreeWill.FreeWill_WorldComponent");
                if (_worldCompType != null)
                {
                    _hasFreeWill = AccessTools.Method(_worldCompType, "HasFreeWill",
                        new[] { typeof(Pawn), typeof(string) });
                    if (_hasFreeWill != null)
                        WBD_Log.Msg("Free Will compatibility enabled (work priorities yield to Free Will).");
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not detect Free Will: " + e.Message);
            }
        }

        public static bool Active
        {
            get
            {
                Resolve();
                return _worldCompType != null && _hasFreeWill != null;
            }
        }

        /// <summary>
        /// True if Free Will is currently managing this pawn's work priorities (so we should not
        /// fight it on the work axis). Returns false if Free Will is absent or anything goes wrong.
        /// </summary>
        public static bool ManagesWork(Pawn pawn)
        {
            if (pawn == null || !Active || Find.World == null)
                return false;
            try
            {
                object worldComp = null;
                var comps = Find.World.components;
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (_worldCompType.IsInstanceOfType(comps[i]))
                        {
                            worldComp = comps[i];
                            break;
                        }
                    }
                }
                if (worldComp == null)
                    return false;
                var result = _hasFreeWill.Invoke(worldComp, new object[] { pawn, pawn.GetUniqueLoadID() });
                return result is bool b && b;
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Free Will check failed for " + pawn.LabelShortCap + ": " + e.Message);
                return false;
            }
        }
    }
}
