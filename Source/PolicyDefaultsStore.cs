using BetterWorkbenchDefaults.Lib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Global, on-disk store of the cross-save "default policies" set (one <see cref="PolicyDefaultsSet"/>),
    /// modeled exactly on <see cref="PawnDefaultsStore"/>: a single side-file next to RimWorld's Config
    /// folder, lazy-loaded + cached, written with the standalone Scribe InitSaving/FinalizeSaving
    /// discipline (mode guards + ForceStop on error) and an edit-a-clone-then-commit pattern so a failed
    /// disk write never desyncs the cache from disk.
    ///
    /// Since 2.0 the set lives in the cross-save library (section "policies"); the 1.x file
    /// <c>PawnDefaults/PolicyDefaults.xml</c> is only read once, by the migration.
    /// </summary>
    public static class PolicyDefaultsStore
    {
        private static int _rev = -1;
        private static PolicyDefaultsSet _cache;

        public static StoredBlock GetStored() => SmdLibraryStore.Content.policies;

        // ---- queries ----

        public static PolicyDefaultsSet GetSet()
        {
            int rev = SmdLibraryStore.Revision;
            if (rev == _rev)
                return _cache;
            var stored = GetStored();
            if (stored == null)
            {
                _cache = null;
                _rev = rev;
                return null;
            }
            var set = new PolicyDefaultsSet();
            if (!SmdScribe.Read(stored.config, set.ExposeData))
            {
                if (SmdScribe.Busy)
                    return null;
                set = new PolicyDefaultsSet();
            }
            set.NormalizeDefault();
            _cache = set.IsEmpty ? null : set;
            _rev = rev;
            return _cache;
        }

        public static bool HasSet()
        {
            var s = GetSet();
            return s != null && !s.IsEmpty;
        }

        // ---- player actions ----

        /// <summary>
        /// Capture the current colony's four policy databases into a fresh set and persist it
        /// cross-save. Requires a loaded game and an idle Scribe. Returns false if it could not run.
        /// </summary>
        public static bool SaveCurrentColony()
        {
            if (Current.Game == null || Scribe.mode != LoadSaveMode.Inactive)
                return false;

            var set = PolicyDefaultsController.CaptureCurrentColony();
            if (set == null || set.IsEmpty)
                return false;
            return Commit(set);
        }

        /// <summary>
        /// Apply the saved set into the current colony: recreate-by-label each policy into the matching
        /// database, then mark the saved default as the colony default (database index 0). Idempotent.
        /// Returns false if there is nothing to apply or the game is busy/unloaded.
        /// </summary>
        public static bool ApplyToCurrentColony()
        {
            if (Current.Game == null || Scribe.mode != LoadSaveMode.Inactive)
                return false;
            var set = GetSet();
            if (set == null || set.IsEmpty)
                return false;
            return PolicyDefaultsController.ApplySetToColony(set);
        }

        /// <summary>Any stored policy block (a profile's) into the current colony, without touching the library.</summary>
        public static bool ApplyBlockToCurrentColony(StoredBlock block)
        {
            if (Current.Game == null || Scribe.mode != LoadSaveMode.Inactive || block?.config == null)
                return false;
            var set = new PolicyDefaultsSet();
            if (!SmdScribe.Read(block.config, set.ExposeData))
                return false;
            set.NormalizeDefault();
            return !set.IsEmpty && PolicyDefaultsController.ApplySetToColony(set);
        }

        public static void ClearAll() =>
            SmdLibraryStore.Mutate(lib =>
            {
                if (lib.content.policies == null)
                    return false;
                lib.content.policies = null;
                return true;
            });

        /// <summary>The running colony's four policy databases as a stored block (for profiles). Null without a game.</summary>
        public static StoredBlock CaptureCurrentColonyBlock()
        {
            if (Current.Game == null || Scribe.mode != LoadSaveMode.Inactive)
                return null;
            var set = PolicyDefaultsController.CaptureCurrentColony();
            return set == null || set.IsEmpty ? null : ToBlock(set);
        }

        private static StoredBlock ToBlock(PolicyDefaultsSet set)
        {
            set.NormalizeDefault();
            var xml = SmdScribe.Write(set.ExposeData);
            if (xml == null)
                return null;
            return new StoredBlock
            {
                config = xml,
                requiredMods = SmdDefRefs.RequiredMods(xml),
                savedAt = System.DateTime.UtcNow.ToString("yyyy-MM-dd")
            };
        }

        /// <summary>Persist the set into the library. Returns false if the write failed.</summary>
        private static bool Commit(PolicyDefaultsSet set)
        {
            var block = ToBlock(set);
            if (block == null)
                return false;
            return SmdLibraryStore.Mutate(lib =>
            {
                lib.content.policies = block;
                return true;
            });
        }
    }
}
