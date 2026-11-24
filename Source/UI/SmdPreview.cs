using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BetterWorkbenchDefaults.Lib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>Which list of the library a building key belongs to (for the "Saved defaults" page and one-line summaries).</summary>
    public enum LibArea { Bills, Storage, Plants, Zones, Other }

    /// <summary>
    /// Readable previews of stored entries, read straight from the stored XML (works for the library, for profiles and for an import
    /// that was never written, also when the mod that owns a def is missing). Never throws; unknown names are shown as they are.
    /// </summary>
    public static class SmdPreview
    {
        const int MaxBillLines = 8;

        // ------------------------------------------------------------------------------------------------ areas
        public static LibArea AreaOf(PresetSet set)
        {
            if (set == null) return LibArea.Other;
            if (SmdContent.IsZoneKey(set.key)) return LibArea.Zones;
            bool bills = false, storage = false, plant = false;
            foreach (var p in set.presets)
            {
                var c = p?.config;
                if (c == null) continue;
                if (El(c, "bills")?.Elements("li").Any() == true) bills = true;
                if (El(c, "storage") != null || El(c, "splitter")?.Elements("li").Any() == true) storage = true;
                if (El(c, "plant") != null) plant = true;
            }
            if (bills) return LibArea.Bills;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(set.key);
            if (def != null && (def.IsWorkTable || typeof(IBillGiver).IsAssignableFrom(def.thingClass))) return LibArea.Bills;
            if (storage) return LibArea.Storage;
            if (plant) return LibArea.Plants;
            return LibArea.Other;
        }

        public static string AreaLabel(LibArea a) => ("WBD_LibArea_" + a).Translate();

        // ------------------------------------------------------------------------------------------------ one line per content
        /// <summary>"4 bill presets for 3 workbenches · 1 shelf preset · 2 colonist presets · policies · colony settings".</summary>
        sealed class LineCache { public int rev; public string lang; public string text; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SmdContent, LineCache> lines = new System.Runtime.CompilerServices.ConditionalWeakTable<SmdContent, LineCache>();

        /// <summary>The one-line summary, cached per content object, library revision and language (asked every frame by the card).</summary>
        public static string OneLine(SmdContent c)
        {
            if (c == null) return OneLineUncached(null);
            int rev = SmdLibraryStore.Revision;
            string lang = LanguageDatabase.activeLanguage?.folderName;
            if (lines.TryGetValue(c, out var lc) && lc.rev == rev && lc.lang == lang) return lc.text;
            string t = OneLineUncached(c);
            lines.AddOrUpdate(c, new LineCache { rev = rev, lang = lang, text = t });
            return t;
        }

        static string OneLineUncached(SmdContent c)
        {
            if (c == null || c.IsEmpty) return "WBD_PvNothing".Translate();
            var parts = new List<string>();
            foreach (LibArea a in Enum.GetValues(typeof(LibArea)))
            {
                var sets = c.buildings.Where(b => AreaOf(b) == a).ToList();
                if (sets.Count == 0) continue;
                parts.Add(Count("WBD_PvCount_" + a, sets.Sum(s => s.presets.Count)));
            }
            if (c.pawns != null && !c.pawns.IsEmpty) parts.Add(Count("WBD_PvCount_Pawns", c.pawns.presets.Count));
            if (c.policies != null) parts.Add("WBD_PvPolicies".Translate());
            if (c.colony != null) parts.Add("WBD_PvColony".Translate());
            return string.Join(" · ", parts);
        }

        // ------------------------------------------------------------------------------------------------ entries
        /// <summary>Preview lines of one preset of a building or zone key.</summary>
        public static List<string> PresetLines(string key, StoredPreset p)
        {
            var l = new List<string>();
            var c = p?.config;
            if (c == null) return l;
            try
            {
                var bills = El(c, "bills")?.Elements("li").ToList() ?? new List<XElement>();
                if (bills.Count > 0)
                {
                    l.Add("WBD_PvBills".Translate(bills.Count));
                    foreach (var b in bills.Take(MaxBillLines)) l.Add("   " + BillLine(b));
                    if (bills.Count > MaxBillLines) l.Add("   " + "WBD_PvMore".Translate(bills.Count - MaxBillLines));
                }
                var st = El(c, "storage");
                if (st != null) l.Add(StorageLine(st));
                var plant = El(c, "plant");
                if (plant != null && El(plant, "plant") != null) l.Add(PlantLine(plant));
                int links = El(c, "splitter")?.Elements("li").Count() ?? 0;
                if (links > 0) l.Add("WBD_PvSplitter".Translate(links));
            }
            catch (Exception e) { l.Add("WBD_PvUnreadable".Translate(e.GetType().Name)); }
            if (l.Count == 0) l.Add("WBD_PvEmptyPreset".Translate());
            return l;
        }

        static string BillLine(XElement b)
        {
            string recipe = (string)b.Element("recipe") ?? "?";
            var def = DefDatabase<RecipeDef>.GetNamedSilentFail(recipe);
            string name = (string)b.Element("customName");
            if (string.IsNullOrEmpty(name)) name = def != null ? def.LabelCap.ToString() : "WBD_PvMissingRecipe".Translate(recipe).ToString();
            string mode = (string)b.Element("repeatMode") ?? "RepeatCount";
            string line;
            if (mode == "TargetCount") line = "WBD_PvBillUntil".Translate(name, Int(b, "targetCount", 10));
            else if (mode == "Forever") line = "WBD_PvBillForever".Translate(name);
            else line = "WBD_PvBillTimes".Translate(name, Int(b, "repeatCount", 1));
            if (Bool(b, "suspended")) line += " " + "WBD_PvSuspended".Translate();
            return line;
        }

        static string StorageLine(XElement st)
        {
            string pr = (string)st.Element("priority");
            string prLabel = Enum.TryParse(pr ?? "Normal", out StoragePriority sp) ? sp.Label() : pr;
            int allowed = st.Element("filter")?.Element("allowedDefs")?.Elements("li").Count() ?? 0;
            var line = "WBD_PvStorage".Translate(prLabel.CapitalizeFirst(), allowed).ToString();
            if (st.Element("intent") != null) line += " · " + "WBD_PvIntent".Translate();
            return line;
        }

        static string PlantLine(XElement pl)
        {
            string plant = (string)pl.Element("plant") ?? "?";
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(plant);
            string line = "WBD_PvPlant".Translate(def != null ? def.LabelCap.ToString() : "WBD_PvMissingDef".Translate(plant).ToString());
            if (Bool(pl, "hasZoneToggles"))
            {
                if (pl.Element("allowSow") != null && !Bool(pl, "allowSow", true)) line += " · " + "WBD_PvSowOff".Translate();
                if (pl.Element("allowCut") != null && !Bool(pl, "allowCut", true)) line += " · " + "WBD_PvCutOff".Translate();
            }
            return line;
        }

        /// <summary>Preview lines of one colonist preset.</summary>
        public static List<string> PawnLines(StoredPreset p)
        {
            var l = new List<string>();
            var c = p?.config;
            if (c == null) return l;
            try
            {
                if (El(c, "timetable") != null) l.Add("WBD_PvSchedule".Translate());
                int jobs = El(c, "work")?.Element("priorities")?.Element("values")?.Elements("li").Count(v => (string)v != "0") ?? 0;
                if (El(c, "work") != null) l.Add("WBD_PvWork".Translate(jobs));
                foreach (var kind in new[] { "apparel", "drug", "food", "reading" })
                {
                    string label = (string)c.Element(kind)?.Element("label");
                    if (!string.IsNullOrEmpty(label)) l.Add(("WBD_PvPolicy_" + kind).Translate(label));
                }
                var misc = El(c, "misc");
                if (misc != null)
                {
                    if (Enum.TryParse((string)misc.Element("hostility") ?? "Flee", out HostilityResponseMode h)) l.Add("WBD_PvHostility".Translate(h.GetLabel()));
                    if (Enum.TryParse((string)misc.Element("medCare") ?? "HerbalOrWorse", out MedicalCareCategory m)) l.Add("WBD_PvMedical".Translate(m.GetLabel()));
                }
            }
            catch (Exception e) { l.Add("WBD_PvUnreadable".Translate(e.GetType().Name)); }
            if (l.Count == 0) l.Add("WBD_PvEmptyPreset".Translate());
            return l;
        }

        /// <summary>Preview lines of a policy set block: one line per kind with its policies, the default marked with a star.</summary>
        public static List<string> PolicyLines(StoredBlock b)
        {
            var l = new List<string>();
            var c = b?.config;
            if (c == null) return l;
            foreach (var kind in PolicyDefaultsController.AllKinds)
            {
                string el = kind.ToString().ToLowerInvariant();
                var labels = c.Element(el)?.Elements("li").Select(li => (string)li.Element("label")).Where(s => !string.IsNullOrEmpty(s)).ToList();
                if (labels == null || labels.Count == 0) continue;
                string def = (string)c.Element(el + "Default");
                l.Add(PolicyDefaultsController.KindLabel(kind) + ": " + string.Join(", ", labels.Select(x => x == def ? "★ " + x : x)));
            }
            if (l.Count == 0) l.Add("WBD_PvEmptyPreset".Translate());
            return l;
        }

        public static List<string> ColonyLines(StoredBlock b)
        {
            try { return ColonySettingsStore.SummaryLines(b); }
            catch (Exception e) { return new List<string> { "WBD_PvUnreadable".Translate(e.GetType().Name) }; }
        }

        /// <summary>Full preview of a content block (profile or import): one line per entry, grouped by area.</summary>
        public static List<string> ContentLines(SmdContent c)
        {
            var l = new List<string>();
            if (c == null || c.IsEmpty) { l.Add("WBD_PvNothing".Translate()); return l; }
            foreach (LibArea a in Enum.GetValues(typeof(LibArea)))
            {
                var sets = c.buildings.Where(b => AreaOf(b) == a).OrderBy(b => DefaultBillsDatabase.KeyLabel(b.key)).ToList();
                if (sets.Count == 0) continue;
                l.Add(AreaLabel(a) + ":");
                foreach (var s in sets)
                    l.Add("   " + DefaultBillsDatabase.KeyLabel(s.key) + ": " + string.Join(", ", s.presets.Select(p => p.name == s.DefaultPreset?.name ? "★ " + p.name : p.name)));
            }
            if (c.pawns != null && !c.pawns.IsEmpty)
                l.Add("WBD_LibArea_Pawns".Translate() + ": " + string.Join(", ", c.pawns.presets.Select(p => p.name == c.pawns.DefaultPreset?.name ? "★ " + p.name : p.name)));
            if (c.policies != null)
            {
                l.Add("WBD_LibArea_Policies".Translate() + ":");
                foreach (var x in PolicyLines(c.policies)) l.Add("   " + x);
            }
            if (c.colony != null)
            {
                l.Add("WBD_LibArea_Colony".Translate() + ":");
                foreach (var x in ColonyLines(c.colony)) l.Add("   " + x);
            }
            return l;
        }

        /// <summary>"Needs Vanilla Cooking Expanded (not active)" for a missing report, or null.</summary>
        public static string MissingMarker(MissingReport m)
        {
            if (m == null || m.IsEmpty) return null;
            if (m.inactiveMods.Count > 0)
                return "WBD_NeedsModMarker".Translate(string.Join(", ", m.inactiveMods.Select(x => string.IsNullOrEmpty(x.name) ? x.id : x.name)));
            return "WBD_UnknownMarker".Translate(m.unknownDefs.Count);
        }

        public static string MissingTip(MissingReport m)
        {
            if (m == null || m.IsEmpty) return null;
            var l = new List<string> { "WBD_MissingTipHead".Translate() };
            foreach (var mod in m.inactiveMods) l.Add("• " + (string.IsNullOrEmpty(mod.name) ? mod.id : mod.name + " (" + mod.id + ")"));
            if (m.unknownDefs.Count > 0)
            {
                l.Add("WBD_MissingTipDefs".Translate(m.unknownDefs.Count));
                foreach (var d in m.unknownDefs.Take(12)) l.Add("   " + d);
                if (m.unknownDefs.Count > 12) l.Add("   …");
            }
            l.Add("WBD_MissingTipKept".Translate());
            return string.Join("\n", l);
        }

        /// <summary>A child element that holds something (Scribe writes absent objects as IsNull="True").</summary>
        static XElement El(XElement parent, string name)
        {
            var e = parent?.Element(name);
            if (e == null || (string)e.Attribute("IsNull") == "True") return null;
            if (!e.HasElements && string.IsNullOrWhiteSpace(e.Value)) return null;
            return e;
        }

        static string Count(string key, int n) => n == 1 ? (key + "_One").Translate().ToString() : key.Translate(n).ToString();

        static int Int(XElement e, string name, int fallback) => int.TryParse((string)e.Element(name), out int v) ? v : fallback;
        static bool Bool(XElement e, string name, bool fallback = false) => bool.TryParse((string)e.Element(name), out bool v) ? v : fallback;
    }
}
