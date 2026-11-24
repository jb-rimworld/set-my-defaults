using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Colony-portable snapshot of a building's storage settings (shelves, stockpile-like
    /// buildings, and any IStoreSettingsParent: vanilla shelves, PRF cold storage / DSU /
    /// puller / smart hopper, etc.). Stores only a def-based ThingFilter + the priority enum,
    /// so it carries across colonies with no save-specific references.
    /// </summary>
    public class StorageSettingsTemplate : IExposable
    {
        public ThingFilter filter;
        public StoragePriority priority = StoragePriority.Normal;

        public StorageSettingsTemplate() { }

        public static StorageSettingsTemplate From(IStoreSettingsParent parent)
        {
            var settings = parent?.GetStoreSettings();
            if (settings == null)
                return null;
            var t = new StorageSettingsTemplate { priority = settings.Priority };
            if (settings.filter != null)
            {
                t.filter = new ThingFilter();
                t.filter.CopyAllowancesFrom(settings.filter);
            }
            return t;
        }

        public void ApplyTo(IStoreSettingsParent parent)
        {
            var settings = parent?.GetStoreSettings();
            if (settings == null)
                return;
            settings.Priority = priority;
            if (filter != null && settings.filter != null)
                settings.filter.CopyAllowancesFrom(filter);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref priority, "priority", StoragePriority.Normal);
            Scribe_Deep.Look(ref filter, "filter");
        }
    }
}
