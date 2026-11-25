using System.Collections.Generic;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The game component of the previous versions (1.x and early 2.0 builds kept the colonist roster in it). Kept with the same class
    /// name only so those saves load: registered with <c>KitRegistry.LegacyComponent</c>, so JB Framework never writes it into a save
    /// and drops its instances once a load or new game finished. Its ExposeData reads the old node and merges it into
    /// <see cref="SmdGameData"/> (union); it has no other job (the roster's game hooks are in <see cref="SmdRoster"/>).
    /// </summary>
    public class PawnDefaultsGameComponent : GameComponent
    {
        public PawnDefaultsGameComponent(Game game) { }

        /// <summary>Reads the node of a save made before 2.0 (or by the 2.0 test builds) and merges it. Never writes.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;
            List<int> seenL = null, coreL = null, workL = null, knownL = null;
            int version = 0, baseline = 0;
            bool colony = false, policy = false;
            Scribe_Collections.Look(ref seenL, "seen", LookMode.Value);
            Scribe_Collections.Look(ref coreL, "appliedCore", LookMode.Value);
            Scribe_Collections.Look(ref workL, "appliedWork", LookMode.Value);
            Scribe_Values.Look(ref policy, "policyDefaultsApplied", false);
            Scribe_Collections.Look(ref knownL, "known", LookMode.Value);
            Scribe_Values.Look(ref version, "rosterVersion", 0);
            Scribe_Values.Look(ref baseline, "baselineTick", 0);
            Scribe_Values.Look(ref colony, "colonyDefaultsApplied", false);
            // JB Framework resets SaveData once per Game object, before the saved components are read, so the merge stays.
            SmdGameData.MergeLegacyRoster(seenL, coreL, workL, knownL, version, baseline, colony, policy);
            WBD_Log.Msg("Carried over the colonist roster of a save from before 2.0 (" + (knownL?.Count ?? 0) + " known, " + (coreL?.Count ?? 0)
                + " defaulted, " + (workL?.Count ?? 0) + " with work priorities). It is saved the new way from now on.");
        }
    }
}
