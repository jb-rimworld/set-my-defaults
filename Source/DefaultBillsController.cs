using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>Player-facing actions for buildings and zones, shared by the Bills-tab button and the gizmos.</summary>
    public static class DefaultBillsController
    {
        /// <summary>Anything we can capture a config from: bills, storage, a plant grower, or a PRF splitter.</summary>
        public static bool Capturable(Thing thing)
        {
            return thing is IBillGiver
                   || thing is IStoreSettingsParent
                   || thing is IPlantToGrowSettable
                   || PrfSplitter.IsSplitter(thing);
        }

        // ================= buildings =================

        public static void OpenDefaultsMenu(Thing thing)
        {
            if (thing == null)
                return;
            var key = thing.def.defName;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("WBD_SaveAsPreset".Translate(), () => PromptSave(thing))
            };

            int targets = SelectedTargets(thing, key).Count;
            var presets = DefaultBillsDatabase.GetStored(key);
            if (presets != null && !presets.IsEmpty)
            {
                foreach (var preset in presets.presets)
                {
                    var name = preset.name;
                    options.Add(new FloatMenuOption(ApplyLabel(name, presets.DefaultPreset?.name == name, null, targets, preset),
                        () => ApplyPreset(thing, key, name)));
                }
                if (presets.presets.Count > 1)
                    options.Add(new FloatMenuOption("WBD_SetDefaultPreset".Translate(), () => OpenSetDefaultMenu(key, DefaultBillsDatabase.KeyLabel(key))));
            }

            // Bench families: presets of related buildings (e.g. the electric stove's presets on a fueled stove).
            foreach (var rel in BenchFamilies.RelatedSavedKeys(thing.def).Take(3))
            {
                var set = DefaultBillsDatabase.GetStored(rel);
                if (set == null)
                    continue;
                var relLabel = DefaultBillsDatabase.KeyLabel(rel);
                foreach (var preset in set.presets)
                {
                    var name = preset.name;
                    var relKey = rel;
                    options.Add(new FloatMenuOption(ApplyLabel(name, false, relLabel, SelectedTargets(thing, relKey).Count, preset),
                        () => ApplyPreset(thing, relKey, name)));
                }
            }

            options.Add(new FloatMenuOption("WBD_OpenLibrary".Translate(), () => OpenLibrary(thing)));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string ApplyLabel(string name, bool isDefault, string fromLabel, int targets, Lib.StoredPreset stored)
        {
            string label = fromLabel != null
                ? "WBD_ApplyFromRelated".Translate(name, fromLabel).ToString()
                : (isDefault ? "WBD_ApplyNamedDefault".Translate(name) : "WBD_ApplyNamed".Translate(name)).ToString();
            if (targets > 1)
                label += " (" + "WBD_ApplyToN".Translate(targets) + ")";
            var missing = SmdDefRefs.Missing(stored);
            if (!missing.IsEmpty)
                label += " [" + missing.Summary() + "]";
            return label;
        }

        /// <summary>
        /// The selected player buildings a preset of <paramref name="key"/> should go to (M2): every selected building of
        /// the same type, plus related buildings when bench families are on. Falls back to the clicked building.
        /// Vanilla merges same-label gizmos and only runs the action of one of them, so the selection is read here.
        /// </summary>
        public static List<Thing> SelectedTargets(Thing clicked, string key)
        {
            var result = new List<Thing>();
            var sel = Find.Selector?.SelectedObjects;
            if (sel != null)
            {
                foreach (var o in sel)
                {
                    if (!(o is Thing t) || !Capturable(t) || t.Faction != Faction.OfPlayerSilentFail)
                        continue;
                    if (t.def.defName == key || (BenchFamilies.Enabled && BenchFamilies.Score(t.def, key) >= BenchFamilies.Threshold))
                        result.Add(t);
                }
            }
            if (clicked != null && !result.Contains(clicked))
                result.Insert(0, clicked);
            return result;
        }

        private static void PromptSave(Thing thing)
        {
            var suggested = DefaultBillsDatabase.HasPresets(thing.def) ? "" : WorkbenchPresets.DefaultName;
            Find.WindowStack.Add(new WBD_Dialog_NamePreset(
                "WBD_SaveAsPromptTitle".Translate(thing.def.LabelCap),
                suggested,
                name => SaveAsPreset(thing, name)));
        }

        public static void SaveAsPreset(Thing thing, string name)
        {
            if (thing == null || name.NullOrEmpty())
                return;
            var config = ConfigSnapshot.Capture(thing);
            if (config == null)
            {
                Messages.Message("WBD_NothingToSave".Translate(thing.def.LabelCap), MessageTypeDefOf.RejectInput, false);
                return;
            }
            bool makeDefault = !DefaultBillsDatabase.HasPresets(thing.def); // first preset becomes the default
            if (!DefaultBillsDatabase.SavePreset(thing.def, name, config, makeDefault))
            {
                Messages.Message(SaveFailedText(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Messages.Message("WBD_PresetSaved".Translate(name, thing.def.LabelCap), MessageTypeDefOf.TaskCompletion, false);
        }

        public static void ApplyPreset(Thing thing, string name) => ApplyPreset(thing, thing?.def?.defName, name);

        /// <summary>Apply preset <paramref name="name"/> of <paramref name="key"/> to the clicked and every eligible selected building.</summary>
        public static void ApplyPreset(Thing thing, string key, string name)
        {
            if (thing == null || key == null)
                return;
            var preset = DefaultBillsDatabase.GetPresets(key)?.Get(name);
            if (preset?.config == null || preset.config.IsEmpty)
            {
                Messages.Message("WBD_NoDefault".Translate(thing.def.LabelCap), MessageTypeDefOf.RejectInput, false);
                return;
            }
            var targets = SelectedTargets(thing, key);
            int done = 0;
            foreach (var t in targets)
            {
                preset.config.ApplyTo(t, replaceBills: true);
                done++;
            }
            var text = done > 1
                ? "WBD_PresetAppliedN".Translate(name, done).ToString()
                : "WBD_PresetApplied".Translate(name).ToString();
            var missing = SmdDefRefs.Missing(DefaultBillsDatabase.GetStored(key)?.Get(name));
            int missingBills = preset.config.MissingRecipeCount();
            if (missingBills > 0)
                text += " " + "WBD_BillsSkippedMissing".Translate(missingBills, missing.IsEmpty ? "?" : missing.Summary());
            Messages.Message(text, MessageTypeDefOf.TaskCompletion, false);
        }

        private static void OpenSetDefaultMenu(string key, string label)
        {
            var presets = DefaultBillsDatabase.GetStored(key);
            if (presets == null || presets.IsEmpty)
                return;
            var options = presets.presets.Select(p =>
            {
                var name = p.name;
                var text = presets.DefaultPreset?.name == name ? "WBD_PresetIsDefault".Translate(name) : (TaggedString)name;
                return new FloatMenuOption(text, () =>
                {
                    if (DefaultBillsDatabase.SetDefaultPreset(key, name))
                        Messages.Message("WBD_DefaultSet".Translate(name, label), MessageTypeDefOf.TaskCompletion, false);
                });
            }).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>The in-game library window, scrolled to this building's list (bills, storage or plants).</summary>
        public static void OpenLibrary(Thing thing)
        {
            string area = null;
            var set = thing != null ? DefaultBillsDatabase.GetStored(thing.def.defName) : null;
            if (set != null && !set.IsEmpty) area = SmdPreview.AreaOf(set).ToString();
            else if (thing is IBillGiver) area = nameof(LibArea.Bills);
            else if (thing is IStoreSettingsParent) area = nameof(LibArea.Storage);
            Dialog_SmdLibrary.Open(SmdLibraryUi.PageLibrary, area);
        }

        internal static string SaveFailedText() =>
            SmdLibraryStore.ReadOnly ? "WBD_LibraryReadOnly".Translate().ToString() : "WBD_SaveBusy".Translate().ToString();

        // ================= zones =================

        /// <summary>The preset keys a zone can save to / apply from.</summary>
        public static List<string> ZoneKeys(Zone zone)
        {
            if (zone is Zone_Stockpile)
                return new List<string> { DefaultBillsDatabase.ZoneStockpile, DefaultBillsDatabase.ZoneDumping };
            if (zone is Zone_Growing)
                return new List<string> { DefaultBillsDatabase.ZoneGrowing };
            return new List<string>();
        }

        public static void OpenZoneMenu(Zone zone)
        {
            if (zone == null)
                return;
            var keys = ZoneKeys(zone);
            if (keys.Count == 0)
                return;
            var options = new List<FloatMenuOption>();
            foreach (var key in keys)
            {
                var k = key;
                options.Add(new FloatMenuOption("WBD_ZoneSaveAs".Translate(DefaultBillsDatabase.KeyLabel(k)), () => PromptSaveZone(zone, k)));
            }
            int targets = SelectedZones(zone).Count;
            foreach (var key in keys)
            {
                var set = DefaultBillsDatabase.GetStored(key);
                if (set == null || set.IsEmpty)
                    continue;
                var keyLabel = DefaultBillsDatabase.KeyLabel(key);
                foreach (var preset in set.presets)
                {
                    var name = preset.name;
                    var k = key;
                    var label = "WBD_ApplyFromRelated".Translate(name, keyLabel).ToString();
                    if (set.DefaultPreset?.name == name)
                        label += " " + "WBD_DefaultMark".Translate();
                    if (targets > 1)
                        label += " (" + "WBD_ApplyToN".Translate(targets) + ")";
                    options.Add(new FloatMenuOption(label, () => ApplyZonePreset(zone, k, name)));
                }
                if (set.presets.Count > 1)
                    options.Add(new FloatMenuOption("WBD_SetDefaultFor".Translate(keyLabel), () => OpenSetDefaultMenu(key, keyLabel)));
            }
            options.Add(new FloatMenuOption("WBD_OpenLibrary".Translate(), () => Dialog_SmdLibrary.Open(SmdLibraryUi.PageLibrary, nameof(LibArea.Zones))));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>The selected zones of the same kind as <paramref name="clicked"/> (falls back to it).</summary>
        public static List<Zone> SelectedZones(Zone clicked)
        {
            var result = new List<Zone>();
            var sel = Find.Selector?.SelectedObjects;
            if (sel != null)
                foreach (var o in sel)
                    if (o is Zone z && z.GetType() == clicked.GetType())
                        result.Add(z);
            if (!result.Contains(clicked))
                result.Insert(0, clicked);
            return result;
        }

        private static void PromptSaveZone(Zone zone, string key)
        {
            var suggested = DefaultBillsDatabase.HasPresets(key) ? "" : WorkbenchPresets.DefaultName;
            Find.WindowStack.Add(new WBD_Dialog_NamePreset(
                "WBD_SaveAsPromptTitle".Translate(DefaultBillsDatabase.KeyLabel(key)),
                suggested,
                name => SaveZonePreset(zone, key, name)));
        }

        public static void SaveZonePreset(Zone zone, string key, string name)
        {
            var config = ConfigSnapshot.CaptureZone(zone);
            if (config == null)
            {
                Messages.Message("WBD_NothingToSave".Translate(zone.label), MessageTypeDefOf.RejectInput, false);
                return;
            }
            bool makeDefault = !DefaultBillsDatabase.HasPresets(key);
            if (!DefaultBillsDatabase.SavePreset(key, name, config, makeDefault))
            {
                Messages.Message(SaveFailedText(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Messages.Message("WBD_PresetSaved".Translate(name, DefaultBillsDatabase.KeyLabel(key)), MessageTypeDefOf.TaskCompletion, false);
        }

        public static void ApplyZonePreset(Zone zone, string key, string name)
        {
            var preset = DefaultBillsDatabase.GetPresets(key)?.Get(name);
            if (preset?.config == null || preset.config.IsEmpty)
            {
                Messages.Message("WBD_NoDefault".Translate(DefaultBillsDatabase.KeyLabel(key)), MessageTypeDefOf.RejectInput, false);
                return;
            }
            int done = 0;
            foreach (var z in SelectedZones(zone))
                if (preset.config.ApplyToZone(z, zoneHasCells: true))
                    done++;
            var text = done > 1
                ? "WBD_PresetAppliedN".Translate(name, done).ToString()
                : "WBD_PresetApplied".Translate(name).ToString();
            var missing = SmdDefRefs.Missing(DefaultBillsDatabase.GetStored(key)?.Get(name));
            if (!missing.IsEmpty)
                text += " " + missing.Summary();
            Messages.Message(text, MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
