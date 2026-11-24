using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// What a filter was MEANT to allow, stored next to the plain def whitelist (M9). A filter is described as an
    /// ordered list of operations on the category tree plus single-def exceptions:
    ///   "+c:Foods"  allow every thing in category Foods (and its sub-categories)
    ///   "-c:Meals"  disallow every thing in category Meals
    ///   "+d:Pemmican" / "-d:Kibble"  single-thing exceptions, applied last
    /// Applying the ops to the CURRENT def tree includes things that a mod added after the preset was saved
    /// ("all Meals" then also means the new modded meals), which a whitelist of defs cannot do.
    ///
    /// Capture is exact for everything that exists at capture time: after building the category ops, every thing whose
    /// result would differ from the captured filter gets an explicit exception. All names are plain strings, so a
    /// category or thing of a missing mod is kept and simply skipped while the mod is off.
    /// </summary>
    public class FilterIntent : IExposable
    {
        public List<string> ops = new List<string>();

        public bool IsEmpty => ops == null || ops.Count == 0;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref ops, "ops", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && ops == null)
                ops = new List<string>();
        }

        // ---- op parsing ----

        public static bool TryParseOp(string op, out bool allow, out bool isCategory, out string defName)
        {
            allow = false;
            isCategory = false;
            defName = null;
            if (string.IsNullOrEmpty(op) || op.Length < 4 || op[2] != ':')
                return false;
            if (op[0] == '+') allow = true;
            else if (op[0] != '-') return false;
            if (op[1] == 'c') isCategory = true;
            else if (op[1] != 'd') return false;
            defName = op.Substring(3);
            return defName.Length > 0;
        }

        private static string Op(bool allow, bool cat, string name) => (allow ? "+" : "-") + (cat ? "c:" : "d:") + name;

        // ---- capture ----

        /// <summary>
        /// Describe <paramref name="filter"/> as category ops + exceptions. <paramref name="parent"/> (the bill's fixed
        /// ingredient filter, the shelf's fixed storage filter) limits the things considered; null = every thing in the
        /// category tree. Returns null when there is nothing to describe.
        /// </summary>
        public static FilterIntent Capture(ThingFilter filter, ThingFilter parent)
        {
            if (filter == null || !ThingCategoryNodeDatabase.initialized || ThingCategoryNodeDatabase.RootNode == null)
                return null;
            try
            {
                var universe = Universe(parent);
                if (universe.Count == 0)
                    return null;
                var allowed = new HashSet<ThingDef>(universe.Where(filter.Allows));

                var intent = new FilterIntent();
                Walk(ThingCategoryNodeDatabase.RootNode, false, universe, allowed, intent.ops);

                // Exceptions: wherever the category ops do not reproduce the captured state exactly.
                var simulated = intent.Resolve(universe);
                foreach (var d in universe.OrderBy(d => d.defName, StringComparer.Ordinal))
                {
                    bool want = allowed.Contains(d);
                    if (simulated.Contains(d) != want)
                        intent.ops.Add(Op(want, false, d.defName));
                }
                return intent;
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not describe a filter by categories (whitelist is used instead): " + e.Message);
                return null;
            }
        }

        private static HashSet<ThingDef> Universe(ThingFilter parent)
        {
            var set = new HashSet<ThingDef>();
            if (parent != null)
            {
                foreach (var d in parent.AllowedThingDefs)
                    if (d != null)
                        set.Add(d);
                return set;
            }
            foreach (var d in ThingCategoryNodeDatabase.RootNode.catDef.DescendantThingDefs)
                if (d != null)
                    set.Add(d);
            return set;
        }

        // Majority rule, top down: inside a denied region a category that is at least half allowed becomes "+c";
        // inside an allowed region one that is less than half allowed becomes "-c". Sub-categories follow the result.
        private static void Walk(TreeNode_ThingCategory node, bool regionAllowed, HashSet<ThingDef> universe,
            HashSet<ThingDef> allowed, List<string> ops)
        {
            if (node?.catDef == null)
                return;
            int total = 0, on = 0;
            foreach (var d in node.catDef.DescendantThingDefs.Distinct())
            {
                if (!universe.Contains(d))
                    continue;
                total++;
                if (allowed.Contains(d))
                    on++;
            }
            if (total == 0)
                return;
            bool state = regionAllowed;
            if (!regionAllowed && on * 2 >= total && on > 0)
            {
                ops.Add(Op(true, true, node.catDef.defName));
                state = true;
            }
            else if (regionAllowed && on * 2 < total)
            {
                ops.Add(Op(false, true, node.catDef.defName));
                state = false;
            }
            if (on == 0 && !state)
                return; // nothing below can be allowed
            if (on == total && state)
                return; // everything below is allowed already
            foreach (var child in node.ChildCategoryNodes)
                Walk(child, state, universe, allowed, ops);
        }

        // ---- resolve / apply ----

        /// <summary>The things these ops allow in the current game, limited to <paramref name="universe"/> when given.</summary>
        public HashSet<ThingDef> Resolve(HashSet<ThingDef> universe)
        {
            var result = new HashSet<ThingDef>();
            if (ops == null)
                return result;
            // categories first (in stored order), then single-thing exceptions (in stored order)
            foreach (var op in ops)
            {
                if (!TryParseOp(op, out bool allow, out bool cat, out string name) || !cat)
                    continue;
                var c = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(name);
                if (c == null)
                    continue;
                foreach (var d in c.DescendantThingDefs)
                {
                    if (allow) result.Add(d);
                    else result.Remove(d);
                }
            }
            foreach (var op in ops)
            {
                if (!TryParseOp(op, out bool allow, out bool cat, out string name) || cat)
                    continue;
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (d == null)
                    continue;
                if (allow) result.Add(d);
                else result.Remove(d);
            }
            if (universe != null)
                result.IntersectWith(universe);
            return result;
        }

        /// <summary>
        /// Make <paramref name="target"/> allow exactly what these ops allow today (special filters, hit points and
        /// quality must already be set, e.g. by CopyAllowancesFrom of the stored whitelist filter).
        /// </summary>
        public void ApplyDefsTo(ThingFilter target, ThingFilter parent)
        {
            if (target == null || IsEmpty || !ThingCategoryNodeDatabase.initialized)
                return;
            var universe = parent != null ? Universe(parent) : null;
            var want = Resolve(universe);
            foreach (var d in target.AllowedThingDefs.ToList())
                if (!want.Contains(d))
                    target.SetAllow(d, false);
            foreach (var d in want)
                if (!target.Allows(d))
                    target.SetAllow(d, true);
        }

        /// <summary>
        /// The one apply path for stored filters: copy the stored whitelist filter (special filters, ranges, defs) and,
        /// when an intent exists and the setting is on, replace the defs by what the intent allows today.
        /// </summary>
        public static void ApplyStored(ThingFilter target, ThingFilter storedWhitelist, FilterIntent intent, ThingFilter parent)
        {
            if (target == null)
                return;
            if (storedWhitelist != null)
                target.CopyAllowancesFrom(storedWhitelist);
            if (intent != null && !intent.IsEmpty && (WBD_Mod.Settings?.filterIntent ?? true))
                intent.ApplyDefsTo(target, parent);
        }

        public static FilterIntent CaptureIfEnabled(ThingFilter filter, ThingFilter parent) =>
            (WBD_Mod.Settings?.filterIntent ?? true) ? Capture(filter, parent) : null;
    }
}
