using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Reflection bridge to Project RimFactory's conveyor splitter
    /// (ProjectRimFactory.AutoMachineTool.Building_BeltSplitter), which is NOT an
    /// IStoreSettingsParent and uses its own per-direction OutputLink config.
    /// Everything is resolved at runtime and fully guarded, so the mod has no compile-time
    /// or load-time dependency on PRF: if PRF is absent (or its API changed) this simply
    /// reports "not a splitter" and does nothing.
    /// </summary>
    internal static class PrfSplitter
    {
        private static bool _resolved;
        private static bool _available;

        private static Type _splitterType;
        private static Type _outputLinkType;
        private static Type _priorityEnumType;
        private static PropertyInfo _outputLinksGetter; // Dictionary<Rot4, OutputLink>
        private static MethodInfo _addOutgoingLink;     // void AddOutgoingLink(Rot4)
        private static PropertyInfo _activeProp;         // bool Active { get; set; }
        private static FieldInfo _priorityField;         // DirectionPriority Priority
        private static FieldInfo _filterField;           // private ThingFilter filter
        private static MethodInfo _copyAllowancesFrom;   // void CopyAllowancesFrom(ThingFilter)

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                _splitterType = GenTypes.GetTypeInAnyAssembly("ProjectRimFactory.AutoMachineTool.Building_BeltSplitter");
                if (_splitterType == null)
                    return;
                _outputLinkType = _splitterType.GetNestedType("OutputLink", BindingFlags.Public | BindingFlags.NonPublic);
                _outputLinksGetter = _splitterType.GetProperty("OutputLinks", BindingFlags.Public | BindingFlags.Instance);
                _addOutgoingLink = _splitterType.GetMethod("AddOutgoingLink", BindingFlags.Public | BindingFlags.Instance);
                if (_outputLinkType == null || _outputLinksGetter == null)
                    return;
                _activeProp = _outputLinkType.GetProperty("Active", BindingFlags.Public | BindingFlags.Instance);
                _priorityField = _outputLinkType.GetField("Priority", BindingFlags.Public | BindingFlags.Instance);
                _filterField = _outputLinkType.GetField("filter", BindingFlags.NonPublic | BindingFlags.Instance);
                _copyAllowancesFrom = _outputLinkType.GetMethod("CopyAllowancesFrom", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(ThingFilter) }, null);
                if (_priorityField != null)
                    _priorityEnumType = _priorityField.FieldType;

                _available = _outputLinksGetter != null && _activeProp != null && _priorityField != null
                             && _filterField != null && _copyAllowancesFrom != null && _addOutgoingLink != null;
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not wire up PRF conveyor splitter support: " + e.Message);
                _available = false;
            }
        }

        public static bool IsSplitter(Thing thing)
        {
            Resolve();
            return _available && thing != null && _splitterType.IsInstanceOfType(thing);
        }

        private static IDictionary OutputLinks(Thing splitter)
        {
            return _outputLinksGetter.GetValue(splitter, null) as IDictionary;
        }

        /// <summary>Capture every configured output direction of a splitter, or null.</summary>
        public static List<SplitterLink> Capture(Thing splitter)
        {
            if (!IsSplitter(splitter))
                return null;
            try
            {
                var dict = OutputLinks(splitter);
                if (dict == null)
                    return null;
                var result = new List<SplitterLink>();
                foreach (DictionaryEntry entry in dict)
                {
                    var rot = (Rot4)entry.Key;
                    var link = entry.Value;
                    if (link == null)
                        continue;
                    var sl = new SplitterLink
                    {
                        rot = rot.AsInt,
                        active = (bool)_activeProp.GetValue(link, null),
                        priority = Convert.ToInt32(_priorityField.GetValue(link))
                    };
                    var f = _filterField.GetValue(link) as ThingFilter;
                    if (f != null)
                    {
                        sl.filter = new ThingFilter();
                        sl.filter.CopyAllowancesFrom(f);
                    }
                    result.Add(sl);
                }
                return result.Count > 0 ? result : null;
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Failed to capture splitter config: " + e.Message);
                return null;
            }
        }

        public static void Apply(Thing splitter, List<SplitterLink> links)
        {
            if (links == null || links.Count == 0 || !IsSplitter(splitter))
                return;
            try
            {
                var dict = OutputLinks(splitter);
                if (dict == null)
                    return;
                foreach (var sl in links)
                {
                    var rot = new Rot4(sl.rot);
                    if (!dict.Contains(rot))
                    {
                        _addOutgoingLink.Invoke(splitter, new object[] { rot });
                        dict = OutputLinks(splitter); // re-fetch in case the collection was replaced
                        if (dict == null || !dict.Contains(rot))
                            continue;
                    }
                    var link = dict[rot];
                    if (link == null)
                        continue;
                    _activeProp.SetValue(link, sl.active, null);
                    if (_priorityEnumType != null)
                        _priorityField.SetValue(link, Enum.ToObject(_priorityEnumType, sl.priority));
                    if (sl.filter != null)
                        _copyAllowancesFrom.Invoke(link, new object[] { sl.filter });
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Failed to apply splitter config: " + e.Message);
            }
        }
    }

    /// <summary>One output direction of a conveyor splitter (portable, def-based).</summary>
    public class SplitterLink : IExposable
    {
        public int rot;        // Rot4.AsInt
        public bool active = true;
        public int priority;   // DirectionPriority underlying int
        public ThingFilter filter;

        public void ExposeData()
        {
            Scribe_Values.Look(ref rot, "rot", 0);
            Scribe_Values.Look(ref active, "active", true);
            Scribe_Values.Look(ref priority, "priority", 0);
            Scribe_Deep.Look(ref filter, "filter");
        }
    }
}
