using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// A portable, def-based snapshot of everything we can remember about a building:
    /// its production bills (IBillGiver), its storage settings (IStoreSettingsParent), and a
    /// PRF conveyor splitter's per-direction routing. Used both for per-workbench-type defaults
    /// and for the per-building config embedded into Blueprints Forked blueprints.
    /// </summary>
    public class ConfigSnapshot : IExposable
    {
        public List<BillTemplate> bills;
        public StorageSettingsTemplate storage;
        public List<SplitterLink> splitterLinks;

        public ConfigSnapshot() { }

        public bool IsEmpty =>
            (bills == null || bills.Count == 0)
            && storage == null
            && (splitterLinks == null || splitterLinks.Count == 0);

        /// <summary>Capture whatever the given thing exposes. Returns null if there's nothing.</summary>
        public static ConfigSnapshot Capture(Thing thing)
        {
            if (thing == null)
                return null;
            var snap = new ConfigSnapshot();

            if (thing is IBillGiver billGiver && billGiver.BillStack != null)
            {
                var captured = BillTemplate.Capture(billGiver.BillStack);
                if (captured.Count > 0)
                    snap.bills = captured;
            }

            if (thing is IStoreSettingsParent storeParent)
                snap.storage = StorageSettingsTemplate.From(storeParent);

            if (PrfSplitter.IsSplitter(thing))
                snap.splitterLinks = PrfSplitter.Capture(thing);

            return snap.IsEmpty ? null : snap;
        }

        /// <summary>
        /// Apply this snapshot to a thing. Returns the number of bills added (for messaging).
        /// Storage and splitter config are applied as side effects.
        /// </summary>
        public int ApplyTo(Thing thing, bool replaceBills, bool includeNonBills = true)
        {
            if (thing == null)
                return 0;

            int billsAdded = 0;
            if (bills != null && bills.Count > 0 && thing is IBillGiver billGiver)
                billsAdded = ApplyBills(thing, billGiver, replaceBills);

            // Storage/splitter are only applied for explicit (manual or blueprint) applies, not
            // for per-type auto-apply on construction, to avoid overwriting a re-spawned
            // building's settings (reinstall / gravship relocation).
            if (includeNonBills)
            {
                if (storage != null && thing is IStoreSettingsParent storeParent)
                    storage.ApplyTo(storeParent);

                if (splitterLinks != null && splitterLinks.Count > 0)
                    PrfSplitter.Apply(thing, splitterLinks);
            }

            return billsAdded;
        }

        private int ApplyBills(Thing thing, IBillGiver billGiver, bool replace)
        {
            var stack = billGiver.BillStack;
            if (stack == null)
                return 0;

            // Build + validate first so a stale/empty default never wipes existing bills.
            var newBills = new List<Bill_Production>();
            foreach (var template in bills)
            {
                if (template == null || !template.AppliesTo(thing.def))
                    continue;
                var bill = template.CreateBill();
                if (bill != null)
                    newBills.Add(bill);
            }
            if (newBills.Count == 0)
                return 0;

            if (replace)
            {
                foreach (var existing in stack.Bills.ToList())
                    stack.Delete(existing);
            }

            int added = 0;
            foreach (var bill in newBills)
            {
                if (stack.Count >= WBD_Mod.Settings.EffectiveBillCap())
                    break;
                stack.AddBill(bill);
                added++;
            }
            NiceBillTabCompat.RequestRefresh(); // otherwise Nice Bill Tab shows its stale cached list
            return added;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref bills, "bills", LookMode.Deep);
            Scribe_Deep.Look(ref storage, "storage");
            Scribe_Collections.Look(ref splitterLinks, "splitter", LookMode.Deep);
        }
    }
}
