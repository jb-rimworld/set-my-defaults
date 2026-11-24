using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Bench families: one preset for every variant of a bench type, vanilla or modded (electric stove, fueled stove,
    /// a modded gas stove ...). Two buildings are related when
    ///   - they share an architect dropdown group (designatorDropdown), or
    ///   - both give bills and their recipe lists overlap by at least 80 % (Jaccard: shared / all).
    /// A building WITHOUT its own preset then uses the default preset of its closest saved relative. A building with
    /// its own presets always uses its own. Recipes the building cannot run are skipped per bill, as always.
    /// If the relative's mod is off, its stored recipe names are compared with the building's recipes instead.
    /// </summary>
    public static class BenchFamilies
    {
        public const float Threshold = 0.8f;

        public static bool Enabled =>
            (WBD_Mod.Settings?.benchFamilies ?? false) && SmdSections.On(SmdSections.Buildings);

        /// <summary>How closely <paramref name="a"/> and <paramref name="b"/> are related, 0..1 (1 = same family for sure).</summary>
        public static float Score(ThingDef a, ThingDef b)
        {
            if (a == null || b == null)
                return 0f;
            if (a == b)
                return 1f;
            if (a.designatorDropdown != null && a.designatorDropdown == b.designatorDropdown && SameStorageParent(a, b))
                return 1f;
            var ra = a.AllRecipes;
            var rb = b.AllRecipes;
            if (ra == null || rb == null || ra.Count == 0 || rb.Count == 0)
                return 0f;
            var sa = new HashSet<RecipeDef>(ra);
            int shared = 0;
            var union = new HashSet<RecipeDef>(ra);
            foreach (var r in rb)
            {
                if (sa.Contains(r))
                    shared++;
                union.Add(r);
            }
            return union.Count == 0 ? 0f : (float)shared / union.Count;
        }

        /// <summary>
        /// Storage buildings are only family if they accept the same things (a shelf preset must not land on a
        /// book-only case in the same dropdown). Buildings without fixed storage settings always pass.
        /// </summary>
        private static bool SameStorageParent(ThingDef a, ThingDef b)
        {
            var fa = a.building?.fixedStorageSettings?.filter;
            var fb = b.building?.fixedStorageSettings?.filter;
            if (fa == null && fb == null)
                return true;
            if (fa == null || fb == null || fa.AllowedDefCount != fb.AllowedDefCount)
                return false;
            foreach (var d in fa.AllowedThingDefs)
                if (!fb.Allows(d))
                    return false;
            return true;
        }

        /// <summary>Score of a saved key against a building: by def when loaded, else by the stored recipe names.</summary>
        public static float Score(ThingDef def, string key)
        {
            if (def == null || key == null || DefaultBillsDatabase.IsZoneKey(key))
                return 0f;
            if (key == def.defName)
                return 1f;
            var keyDef = DefDatabase<ThingDef>.GetNamedSilentFail(key);
            if (keyDef != null)
                return Score(def, keyDef);
            // The relative's mod is off: compare the recipe names stored in its default preset.
            var stored = DefaultBillsDatabase.GetStored(key)?.DefaultPreset;
            var names = stored?.config?.Element("bills")?.Elements("li")
                .Select(li => (string)li.Element("recipe"))
                .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            if (names == null || names.Count == 0 || def.AllRecipes == null || def.AllRecipes.Count == 0)
                return 0f;
            var mine = new HashSet<string>(def.AllRecipes.Select(r => r.defName));
            return (float)names.Count(mine.Contains) / names.Count;
        }

        /// <summary>
        /// The saved key whose default preset a building without own presets should use, or null.
        /// Highest score wins; ties are broken by key name so the choice is stable.
        /// </summary>
        private static readonly Dictionary<ThingDef, KeyValuePair<int, string>> _fallbackCache =
            new Dictionary<ThingDef, KeyValuePair<int, string>>();

        public static string FallbackKeyFor(ThingDef def)
        {
            if (!Enabled || def == null)
                return null;
            // cached per library revision and recipe count (PRF assemblers change their recipe list at runtime)
            int stamp = SmdLibraryStore.Revision * 4099 + (def.AllRecipes?.Count ?? 0);
            if (_fallbackCache.TryGetValue(def, out var hit) && hit.Key == stamp)
                return hit.Value;
            var found = FindFallback(def);
            _fallbackCache[def] = new KeyValuePair<int, string>(stamp, found);
            return found;
        }

        private static string FindFallback(ThingDef def)
        {
            string best = null;
            float bestScore = 0f;
            foreach (var key in DefaultBillsDatabase.AllSavedDefNames())
            {
                if (key == def.defName || DefaultBillsDatabase.IsZoneKey(key))
                    continue;
                float s = Score(def, key);
                if (s < Threshold)
                    continue;
                bool better = best == null || s > bestScore + 0.0001f
                              || (Math.Abs(s - bestScore) <= 0.0001f && string.CompareOrdinal(key, best) < 0);
                if (better)
                {
                    best = key;
                    bestScore = s;
                }
            }
            return best;
        }

        /// <summary>Saved keys related to <paramref name="def"/> (excluding its own), best first.</summary>
        public static List<string> RelatedSavedKeys(ThingDef def)
        {
            if (!Enabled || def == null)
                return new List<string>();
            return DefaultBillsDatabase.AllSavedDefNames()
                .Where(k => k != def.defName && !DefaultBillsDatabase.IsZoneKey(k))
                .Select(k => new { k, s = Score(def, k) })
                .Where(x => x.s >= Threshold)
                .OrderByDescending(x => x.s).ThenBy(x => x.k, StringComparer.Ordinal)
                .Select(x => x.k).ToList();
        }

        /// <summary>Every loaded building a preset of <paramref name="key"/> would reach through its family (for the UI).</summary>
        public static List<ThingDef> Members(string key)
        {
            var result = new List<ThingDef>();
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.category != ThingCategory.Building || d.IsBlueprint || d.IsFrame)
                    continue;
                if (d.defName == key || (Enabled && Score(d, key) >= Threshold))
                    result.Add(d);
            }
            return result;
        }
    }
}
