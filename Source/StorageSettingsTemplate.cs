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
        // 2.0: what the filter MEANT (categories + exceptions), so new modded items of a category are included.
        public FilterIntent intent;

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
                t.intent = FilterIntent.CaptureIfEnabled(settings.filter, ParentFilter(parent));
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
                FilterIntent.ApplyStored(settings.filter, filter, intent, ParentFilter(parent));
        }

        /// <summary>Apply to a bare StorageSettings (a storage blueprint before it has a parent of its own).</summary>
        public void ApplyTo(StorageSettings settings, ThingFilter parentFilter)
        {
            if (settings == null)
                return;
            settings.Priority = priority;
            if (filter != null && settings.filter != null)
                FilterIntent.ApplyStored(settings.filter, filter, intent, parentFilter);
        }

        private static ThingFilter ParentFilter(IStoreSettingsParent parent)
        {
            try
            {
                return parent?.GetParentStoreSettings()?.filter;
            }
            catch
            {
                return null;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref priority, "priority", StoragePriority.Normal);
            Scribe_Deep.Look(ref filter, "filter");
            Scribe_Deep.Look(ref intent, "intent");
        }
    }
}
