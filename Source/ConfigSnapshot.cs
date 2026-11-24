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
    /// <summary>Which parts of a <see cref="ConfigSnapshot"/> an apply touches.</summary>
    [System.Flags]
    public enum ConfigParts
    {
        None = 0,
        Bills = 1,
        Storage = 2,
        Splitter = 4,
        Plant = 8,
        All = Bills | Storage | Splitter | Plant
    }

    public class ConfigSnapshot : IExposable
    {
        public List<BillTemplate> bills;
        public StorageSettingsTemplate storage;
        public List<SplitterLink> splitterLinks;
        public PlantTemplate plant; // 2.0 (M10): growing zone / plant grower plant

        public ConfigSnapshot() { }

        public bool IsEmpty =>
            (bills == null || bills.Count == 0)
            && storage == null
            && (splitterLinks == null || splitterLinks.Count == 0)
            && (plant == null || plant.IsEmpty);

        /// <summary>Capture a zone: a stockpile's storage settings or a growing zone's plant.</summary>
        public static ConfigSnapshot CaptureZone(Zone zone)
        {
            if (zone == null)
                return null;
            var snap = new ConfigSnapshot();
            if (zone is IStoreSettingsParent storeParent)
                snap.storage = StorageSettingsTemplate.From(storeParent);
            if (zone is IPlantToGrowSettable settable)
                snap.plant = PlantTemplate.Capture(settable);
            return snap.IsEmpty ? null : snap;
        }

        /// <summary>Apply to a zone. Returns true when anything was applied.</summary>
        public bool ApplyToZone(Zone zone, bool zoneHasCells)
        {
            if (zone == null)
                return false;
            bool any = false;
            if (storage != null && zone is IStoreSettingsParent storeParent)
            {
                storage.ApplyTo(storeParent);
                any = true;
            }
            if (plant != null && zone is IPlantToGrowSettable settable)
                any |= plant.ApplyTo(settable, zoneHasCells) || plant.hasZoneToggles;
            return any;
        }

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

            if (thing is IPlantToGrowSettable settable)
                snap.plant = PlantTemplate.Capture(settable);

            return snap.IsEmpty ? null : snap;
        }

        /// <summary>
        /// Apply this snapshot to a thing. Returns the number of bills added (for messaging).
        /// <paramref name="includeNonBills"/> = false is the 1.x "bills only" auto-apply.
        /// </summary>
        public int ApplyTo(Thing thing, bool replaceBills, bool includeNonBills = true) =>
            ApplyTo(thing, replaceBills, includeNonBills ? ConfigParts.All : ConfigParts.Bills);

        public int ApplyTo(Thing thing, bool replaceBills, ConfigParts parts)
        {
            if (thing == null)
                return 0;

            int billsAdded = 0;
            if ((parts & ConfigParts.Bills) != 0 && bills != null && bills.Count > 0 && thing is IBillGiver billGiver)
                billsAdded = ApplyBills(thing, billGiver, replaceBills);

            if ((parts & ConfigParts.Storage) != 0 && storage != null && thing is IStoreSettingsParent storeParent)
                storage.ApplyTo(storeParent);

            if ((parts & ConfigParts.Splitter) != 0 && splitterLinks != null && splitterLinks.Count > 0)
                PrfSplitter.Apply(thing, splitterLinks);

            if ((parts & ConfigParts.Plant) != 0 && plant != null && thing is IPlantToGrowSettable settable)
                plant.ApplyTo(settable);

            return billsAdded;
        }

        /// <summary>Number of stored bills whose recipe does not exist in this game (mod off).</summary>
        public int MissingRecipeCount()
        {
            int n = 0;
            if (bills != null)
                foreach (var b in bills)
                    if (b != null && !b.IsValid)
                        n++;
            return n;
        }

        private int ApplyBills(Thing thing, IBillGiver billGiver, bool replace)
        {
            var stack = billGiver.BillStack;
            if (stack == null)
                return 0;

            // Build + validate first so a stale/empty default never wipes existing bills.
            var newBills = new List<KeyValuePair<BillTemplate, Bill_Production>>();
            foreach (var template in bills)
            {
                if (template == null || !template.AppliesTo(thing.def))
                    continue;
                var bill = template.CreateBill();
                if (bill != null)
                    newBills.Add(new KeyValuePair<BillTemplate, Bill_Production>(template, bill));
            }
            if (newBills.Count == 0)
                return 0;

            if (replace)
            {
                foreach (var existing in stack.Bills.ToList())
                    stack.Delete(existing);
            }

            int added = 0;
            foreach (var pair in newBills)
            {
                if (stack.Count >= WBD_Mod.Settings.EffectiveBillCap())
                    break;
                stack.AddBill(pair.Value);
                pair.Key.ApplyMapBound(pair.Value);
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
            Scribe_Deep.Look(ref plant, "plant");
        }
    }
}
