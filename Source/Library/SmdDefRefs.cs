using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BetterWorkbenchDefaults.Lib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>What a stored entry needs that this game does not have.</summary>
    public sealed class MissingReport
    {
        /// <summary>Recorded mods of the entry that are not active now (id + name as saved).</summary>
        public List<ModRef> inactiveMods = new List<ModRef>();
        /// <summary>Def names in the entry that do not exist in this game ("RecipeDef:CookVCEStew").</summary>
        public List<string> unknownDefs = new List<string>();

        public bool IsEmpty => inactiveMods.Count == 0 && unknownDefs.Count == 0;

        public void Merge(MissingReport other)
        {
            if (other == null)
                return;
            foreach (var m in other.inactiveMods)
                if (inactiveMods.All(x => x.id != m.id))
                    inactiveMods.Add(m);
            foreach (var d in other.unknownDefs)
                if (!unknownDefs.Contains(d))
                    unknownDefs.Add(d);
        }

        /// <summary>One line for messages and tooltips, e.g. "needs Vanilla Cooking Expanded; 3 unknown entries".</summary>
        public string Summary()
        {
            if (IsEmpty)
                return "";
            var parts = new List<string>();
            if (inactiveMods.Count > 0)
                parts.Add("WBD_MissingNeedsMods".Translate(string.Join(", ", inactiveMods.Select(m => string.IsNullOrEmpty(m.name) ? m.id : m.name))).ToString());
            if (unknownDefs.Count > 0)
                parts.Add("WBD_MissingUnknownDefs".Translate(unknownDefs.Count).ToString());
            return string.Join("; ", parts);
        }
    }

    /// <summary>
    /// Finds every def name inside a stored &lt;config&gt; element, by element name. Used (1) when saving, to record
    /// which mods an entry needs, and (2) when listing/applying, to name what is missing.
    /// </summary>
    public static class SmdDefRefs
    {
        // element name (for single values) or parent name (for "li" list items) -> def type
        private static readonly Dictionary<string, Type> Single = new Dictionary<string, Type>
        {
            { "recipe", typeof(RecipeDef) },
            { "repeatMode", typeof(BillRepeatModeDef) },
            { "storeMode", typeof(BillStoreModeDef) },
            { "style", typeof(ThingStyleDef) },
            { "overrideRootDef", typeof(ThingCategoryDef) },
            { "drug", typeof(ThingDef) },
            { "plant", typeof(ThingDef) },
        };

        private static readonly Dictionary<string, Type> ListItems = new Dictionary<string, Type>
        {
            { "allowedDefs", typeof(ThingDef) },
            { "disallowedSpecialFilters", typeof(SpecialThingFilterDef) },
            { "assignments", typeof(TimeAssignmentDef) },
        };

        /// <summary>All (defType, defName) pairs referenced by <paramref name="config"/>.</summary>
        public static IEnumerable<KeyValuePair<Type, string>> Collect(XElement config)
        {
            if (config == null)
                yield break;
            foreach (var e in config.DescendantsAndSelf())
            {
                if (e.HasElements)
                {
                    // work priorities: <priorities><keys><li>Cooking</li>...</keys>
                    if (e.Name.LocalName == "keys" && e.Parent?.Name.LocalName == "priorities")
                        foreach (var li in e.Elements("li"))
                            yield return new KeyValuePair<Type, string>(typeof(WorkTypeDef), li.Value.Trim());
                    continue;
                }
                var name = e.Name.LocalName;
                var text = e.Value?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;
                Type t;
                if (name == "li" && e.Parent != null)
                {
                    var parent = e.Parent.Name.LocalName;
                    if (ListItems.TryGetValue(parent, out t))
                        yield return new KeyValuePair<Type, string>(t, text);
                    else if (parent == "ops" && FilterIntent.TryParseOp(text, out _, out bool isCat, out string defName))
                        yield return new KeyValuePair<Type, string>(isCat ? typeof(ThingCategoryDef) : typeof(ThingDef), defName);
                }
                else if (Single.TryGetValue(name, out t))
                {
                    yield return new KeyValuePair<Type, string>(t, text);
                }
            }
        }

        private static Def Resolve(Type t, string name)
        {
            try
            {
                return GenDefDatabase.GetDefSilentFail(t, name, false);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The non-core mods (DLCs included) that own defs referenced by <paramref name="config"/>.</summary>
        public static List<ModRef> RequiredMods(XElement config)
        {
            var result = new List<ModRef>();
            var seen = new HashSet<string>();
            foreach (var kv in Collect(config))
            {
                var def = Resolve(kv.Key, kv.Value);
                var pack = def?.modContentPack;
                if (pack == null || pack.IsCoreMod)
                    continue;
                var id = pack.PackageIdPlayerFacing?.ToLowerInvariant();
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    continue;
                result.Add(new ModRef(id, pack.Name));
            }
            return result;
        }

        /// <summary>What <paramref name="config"/> needs that is missing now.</summary>
        public static MissingReport Missing(XElement config, List<ModRef> requiredMods)
        {
            var r = new MissingReport();
            if (requiredMods != null)
                foreach (var m in requiredMods)
                    if (!string.IsNullOrEmpty(m.id) && !IsModActive(m.id) && r.inactiveMods.All(x => x.id != m.id))
                        r.inactiveMods.Add(m);
            var seen = new HashSet<string>();
            foreach (var kv in Collect(config))
            {
                if (Resolve(kv.Key, kv.Value) != null)
                    continue;
                var label = kv.Key.Name + ":" + kv.Value;
                if (seen.Add(label))
                    r.unknownDefs.Add(label);
            }
            return r;
        }

        public static MissingReport Missing(StoredPreset p) => p == null ? new MissingReport() : Missing(p.config, p.requiredMods);
        public static MissingReport Missing(StoredBlock b) => b == null ? new MissingReport() : Missing(b.config, b.requiredMods);

        public static bool IsModActive(string packageId)
        {
            try
            {
                return ModsConfig.IsActive(packageId);
            }
            catch
            {
                return false;
            }
        }
    }
}
