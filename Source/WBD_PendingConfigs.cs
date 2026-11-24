using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Per-map registry of configs queued by a blueprint placement, applied when the matching
    /// building finishes construction. Auto-created for every map and saved with the colony.
    ///
    /// Lifecycle (self-cleaning, bounded):
    ///  - applied immediately when the building spawns (NotifySpawned);
    ///  - a throttled tick re-applies any still-missing bills (deduped) for a bounded window —
    ///    this rescues PRF "smart" assemblers whose database-fed recipes only appear shortly
    ///    after spawn;
    ///  - an entry is dropped if its construction is cancelled (no building/frame/blueprint left
    ///    at the cell), if the built building is later removed, or once the retry window passes.
    /// This prevents both stale-entry leaks and a cancelled blueprint clobbering a later build.
    /// </summary>
    public class WBD_PendingConfigs : MapComponent
    {
        // Since 2.0 the entries live in SmdGameData (saved inside JB Framework's component); this component only reads the node of a
        // save from before 2.0 and is never written again (Patch_Map_ExposeComponents).
        private List<PendingEntry> _entries => SmdGameData.PendingFor(map);

        private const int TickInterval = 250;
        private const int RetryWindowTicks = 12000; // ~3.3 in-game hours of retrying missing bills

        public WBD_PendingConfigs(Map map) : base(map) { }

        public static WBD_PendingConfigs For(Map map)
        {
            return map?.GetComponent<WBD_PendingConfigs>();
        }

        public void Register(IntVec3 cell, ThingDef def, ConfigSnapshot snapshot)
        {
            if (def == null || snapshot == null || snapshot.IsEmpty)
                return;
            _entries.RemoveAll(e => e.def == def && e.cell == cell);
            _entries.Add(new PendingEntry { cell = cell, def = def, snapshot = snapshot });
        }

        public bool HasAny => _entries.Count > 0;

        /// <summary>
        /// Apply a matching pending config to a freshly-spawned building. Returns true if an
        /// entry matched (so the caller skips the per-type default).
        /// </summary>
        public bool NotifySpawned(Building building)
        {
            if (building == null || _entries.Count == 0)
                return false;
            var rect = building.OccupiedRect();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.def != building.def)
                    continue;
                if (e.cell == building.Position || rect.Contains(e.cell))
                {
                    ApplyEntry(e, building);
                    e.spawned = true;
                    if (IsComplete(e, building))
                        _entries.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public override void MapComponentTick()
        {
            if (_entries.Count == 0)
                return;
            if (Find.TickManager.TicksGame % TickInterval != 0)
                return;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                var building = FindBuilding(e);

                if (building != null)
                {
                    ApplyEntry(e, building);
                    e.spawned = true;
                    e.ticksSinceSpawn += TickInterval;
                    if (IsComplete(e, building) || e.ticksSinceSpawn > RetryWindowTicks)
                        _entries.RemoveAt(i);
                }
                else if (e.spawned)
                {
                    // Was built, now gone (deconstructed/destroyed) -> drop the entry.
                    _entries.RemoveAt(i);
                }
                else if (!ConstructionPending(e))
                {
                    // Never built and nothing queued at the cell -> blueprint/frame was cancelled.
                    _entries.RemoveAt(i);
                }
            }
        }

        private void ApplyEntry(PendingEntry e, Building b)
        {
            if (e.snapshot.bills != null && e.snapshot.bills.Count > 0
                && b is IBillGiver billGiver && billGiver.BillStack != null)
                AddMissingBills(e, billGiver);

            if (!e.nonBillsApplied)
            {
                if (e.snapshot.storage != null && b is IStoreSettingsParent storeParent)
                    e.snapshot.storage.ApplyTo(storeParent);
                if (e.snapshot.splitterLinks != null && e.snapshot.splitterLinks.Count > 0)
                    PrfSplitter.Apply(b, e.snapshot.splitterLinks);
                if (e.snapshot.plant != null && b is IPlantToGrowSettable settable)
                    e.snapshot.plant.ApplyTo(settable);
                e.nonBillsApplied = true;
            }
        }

        // Add each bill template at most ONCE, tracked per template index (not per recipe), so
        // intentional duplicate bills with the same recipe but different filters/counts are all
        // recreated, and retries only attempt templates not yet applied.
        private static void AddMissingBills(PendingEntry e, IBillGiver billGiver)
        {
            var templates = e.snapshot.bills;
            EnsureAppliedFlags(e, templates.Count);
            var def = ((Thing)billGiver).def;
            var stack = billGiver.BillStack;

            for (int i = 0; i < templates.Count; i++)
            {
                if (e.applied[i])
                    continue;
                var template = templates[i];
                if (template == null || !template.IsValid)
                {
                    e.applied[i] = true; // missing/removed recipe -> give up on this one
                    continue;
                }
                if (!template.AppliesTo(def))
                    continue; // not available yet -> try again on a later tick
                if (stack.Count >= WBD_Mod.Settings.EffectiveBillCap())
                    break;
                var bill = template.CreateBill();
                if (bill != null)
                {
                    stack.AddBill(bill);
                    template.ApplyMapBound(bill);
                    SmdCompat.RefreshBillsTab();
                }
                e.applied[i] = true;
            }
        }

        private static void EnsureAppliedFlags(PendingEntry e, int count)
        {
            if (e.applied == null || e.applied.Count != count)
            {
                e.applied = new List<bool>(count);
                for (int i = 0; i < count; i++)
                    e.applied.Add(false);
            }
        }

        // Complete once non-bill settings are applied and every bill template has been handled
        // (applied, or given up on because its recipe is gone). Templates whose recipe exists but
        // isn't available yet stay unhandled and keep the entry alive until the retry window ends.
        private bool IsComplete(PendingEntry e, Building b)
        {
            if (!e.nonBillsApplied)
                return false;
            if (e.snapshot.bills == null || e.snapshot.bills.Count == 0)
                return true;
            if (e.applied == null || e.applied.Count != e.snapshot.bills.Count)
                return false;
            for (int i = 0; i < e.applied.Count; i++)
            {
                if (!e.applied[i])
                    return false;
            }
            return true;
        }

        private Building FindBuilding(PendingEntry e)
        {
            if (e.def == null)
                return null;
            var things = e.cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building b && b.def == e.def && b.Spawned)
                    return b;
            }
            return null;
        }

        // Is there still a frame / build-blueprint queued for this def at the cell?
        private bool ConstructionPending(PendingEntry e)
        {
            if (e.def == null)
                return false;
            var things = e.cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i]?.def?.entityDefToBuild == e.def)
                    return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode != LoadSaveMode.LoadingVars && Scribe.mode != LoadSaveMode.ResolvingCrossRefs && Scribe.mode != LoadSaveMode.PostLoadInit)
                return;
            Scribe_Collections.Look(ref _legacy, "wbdPending", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (_legacy != null)
                {
                    _legacy.RemoveAll(e => e == null || e.def == null || e.snapshot == null || e.snapshot.IsEmpty);
                    SmdGameData.PendingFor(map).AddRange(_legacy);
                }
                _legacy = null;
            }
        }

        private List<PendingEntry> _legacy;
    }

    public class PendingEntry : IExposable
    {
        public IntVec3 cell;
        public ThingDef def;
        public ConfigSnapshot snapshot;
        public bool nonBillsApplied;
        public bool spawned;
        public int ticksSinceSpawn;
        public List<bool> applied; // per bill-template "already added" flags (parallel to snapshot.bills)

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Defs.Look(ref def, "def");
            Scribe_Deep.Look(ref snapshot, "config");
            Scribe_Values.Look(ref nonBillsApplied, "nonBillsApplied", false);
            Scribe_Values.Look(ref spawned, "spawned", false);
            Scribe_Values.Look(ref ticksSinceSpawn, "ticks", 0);
            Scribe_Collections.Look(ref applied, "appliedFlags", LookMode.Value);
        }
    }
}
