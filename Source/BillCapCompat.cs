using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Interop with mods that raise RimWorld's per-workbench bill limit (vanilla <c>BillStack.MaxCount</c> == 15): KiameV's original
    /// "No Max Bills" and "No Max Bills Redux" (justharry.nomaxbillsredux). Both define the marker type
    /// <c>NoMaxBills.Patch_BillStack_DoListing</c>; Redux is also found by its packageId.
    ///
    /// <para>Installed is not enough: the 1.6 build of No Max Bills Redux fails in its own constructor on RimWorld 1.7 (one of its
    /// patch targets is gone, so Harmony's PatchAll throws) and then may raise nothing. So the limit belongs to that mod only while
    /// another mod's transpiler really sits on <c>BillStack.DoListing</c> or <c>ITab_Bills.FillTab</c> (<see cref="Working"/>).
    /// Installed but not working (<see cref="Broken"/>) = this mod's own transpiler takes over and lifts the limit to "Bills per
    /// workbench", because the player installed it to get more than 15 bills; the Compatibility page says so.</para>
    /// </summary>
    public static class BillCapCompat
    {
        private const string MarkerType = "NoMaxBills.Patch_BillStack_DoListing";
        private const string ReduxPackageId = "justharry.nomaxbillsredux";

        private static bool _resolved, _installed;

        /// <summary>A No Max Bills mod is installed (marker type or Redux packageId), whether or not its patches applied.</summary>
        public static bool Installed { get { Resolve(); return _installed; } }

        /// <summary>Installed and its transpiler is in place: it owns the limit, this mod adds no patch.</summary>
        public static bool Active => Installed && ForeignCapPatch();

        /// <summary>Same as <see cref="Active"/> (the name the Compatibility page check uses).</summary>
        public static bool Working => Active;

        /// <summary>Installed but none of its patches applied (its 1.6 build on 1.7): this mod's own transpiler lifts the limit instead.</summary>
        public static bool Broken => Installed && !ForeignCapPatch();

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                if (GenTypes.GetTypeInAnyAssembly(MarkerType) != null)
                    _installed = true;
                else
                {
                    var mods = LoadedModManager.RunningModsListForReading;
                    if (mods != null)
                        _installed = mods.Any(m => m?.PackageIdPlayerFacing != null && m.PackageIdPlayerFacing.EqualsIgnoreCase(ReduxPackageId));
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not detect a No Max Bills mod: " + e.Message);
            }
        }

        /// <summary>Another mod's transpiler on one of the two methods that gate the bill count (our own patch does not count).</summary>
        private static bool ForeignCapPatch()
        {
            try
            {
                foreach (var m in Patch_BillStack_RaiseCap.TargetMethods())
                {
                    var info = Harmony.GetPatchInfo(m);
                    if (info?.Transpilers != null && info.Transpilers.Any(p => p.owner != null && !p.owner.StartsWith(WBD_Mod.PackageId, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not inspect the bill-limit patches of No Max Bills: " + e.Message);
            }
            return false;
        }

        /// <summary>One line for the log, the Diagnosis page and the Compatibility page.</summary>
        public static string State =>
            !Installed ? "not installed" : Active ? "installed and working: it owns the bill limit" : "installed but not working (its patches did not apply): Set my Defaults lifts the limit instead";
    }
}
