using System;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Reflection-only interop detector for mods that already raise RimWorld's per-workbench bill
    /// limit (vanilla <c>BillStack.MaxCount</c> == 15). Both KiameV's original "No Max Bills" and
    /// "No Max Bills Redux" (justharry.nomaxbillsredux) define the marker type
    /// <c>NoMaxBills.Patch_BillStack_DoListing</c> (Redux ships it via NoMaxBillsMimicry so Falconne's
    /// Better Workbench Management raises its own cap). We probe for that type, and as a fallback scan
    /// the running mod list for the Redux packageId.
    ///
    /// Purpose: (1) so our own self-contained transpiler NEVER registers when an external mod already
    /// raised the UI cap (avoids redundant double-patching of BillStack.DoListing / ITab_Bills.FillTab),
    /// and (2) so <see cref="WBD_Settings.EffectiveBillCap"/> returns the player's chosen cap whenever
    /// EITHER our own feature or an external cap-raiser is active, keeping the visible UI cap and the
    /// number of bills we restore in agreement.
    /// </summary>
    public static class BillCapCompat
    {
        private const string MarkerType = "NoMaxBills.Patch_BillStack_DoListing";
        private const string ReduxPackageId = "justharry.nomaxbillsredux";

        private static bool _resolved;
        private static bool _active;

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                // Either No Max Bills (original) or No Max Bills Redux defines this marker type.
                if (GenTypes.GetTypeInAnyAssembly(MarkerType) != null)
                {
                    _active = true;
                }
                else
                {
                    var mods = LoadedModManager.RunningModsListForReading;
                    if (mods != null)
                    {
                        for (int i = 0; i < mods.Count; i++)
                        {
                            var pid = mods[i]?.PackageIdPlayerFacing;
                            if (pid != null && pid.EqualsIgnoreCase(ReduxPackageId))
                            {
                                _active = true;
                                break;
                            }
                        }
                    }
                }

                if (_active)
                    WBD_Log.Msg("No Max Bills detected; the built-in bill-cap raiser will stay off (interop, no double-patch).");
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not detect a No Max Bills mod: " + e.Message);
            }
        }

        /// <summary>True when an external mod already raised the workbench bill cap.</summary>
        public static bool Active
        {
            get
            {
                Resolve();
                return _active;
            }
        }
    }
}
