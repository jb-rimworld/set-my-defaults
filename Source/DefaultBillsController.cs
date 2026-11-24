using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>Player-facing actions, shared by the Bills-tab button and the gizmo.</summary>
    public static class DefaultBillsController
    {
        /// <summary>Anything we can capture a config from: bills, storage, or a PRF splitter.</summary>
        public static bool Capturable(Thing thing)
        {
            return thing is IBillGiver
                   || thing is IStoreSettingsParent
                   || PrfSplitter.IsSplitter(thing);
        }

        public static void OpenDefaultsMenu(Thing thing)
        {
            if (thing == null)
                return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("WBD_SaveAsPreset".Translate(), () => PromptSave(thing))
            };

            var presets = DefaultBillsDatabase.GetPresets(thing.def);
            if (presets != null && !presets.IsEmpty)
            {
                foreach (var preset in presets.presets)
                {
                    var name = preset.name;
                    var label = presets.IsDefault(name)
                        ? "WBD_ApplyNamedDefault".Translate(name)
                        : "WBD_ApplyNamed".Translate(name);
                    options.Add(new FloatMenuOption(label, () => ApplyPreset(thing, name)));
                }

                if (presets.presets.Count > 1)
                    options.Add(new FloatMenuOption("WBD_SetDefaultPreset".Translate(), () => OpenSetDefaultMenu(thing)));

                options.Add(new FloatMenuOption("WBD_ManagePresets".Translate(), OpenSettings));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void PromptSave(Thing thing)
        {
            var presets = DefaultBillsDatabase.GetPresets(thing.def);
            var suggested = (presets == null || presets.IsEmpty) ? WorkbenchPresets.DefaultName : "";
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
                Messages.Message("WBD_SaveBusy".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Messages.Message("WBD_PresetSaved".Translate(name, thing.def.LabelCap), MessageTypeDefOf.TaskCompletion, false);
        }

        public static void ApplyPreset(Thing thing, string name)
        {
            if (thing == null)
                return;
            var preset = DefaultBillsDatabase.GetPresets(thing.def)?.Get(name);
            if (preset?.config == null || preset.config.IsEmpty)
            {
                Messages.Message("WBD_NoDefault".Translate(thing.def.LabelCap), MessageTypeDefOf.RejectInput, false);
                return;
            }
            preset.config.ApplyTo(thing, replaceBills: true);
            Messages.Message("WBD_PresetApplied".Translate(name), MessageTypeDefOf.TaskCompletion, false);
        }

        private static void OpenSetDefaultMenu(Thing thing)
        {
            var presets = DefaultBillsDatabase.GetPresets(thing.def);
            if (presets == null || presets.IsEmpty)
                return;
            var options = presets.presets.Select(p =>
            {
                var name = p.name;
                var label = presets.IsDefault(name) ? "WBD_PresetIsDefault".Translate(name) : (TaggedString)name;
                return new FloatMenuOption(label, () =>
                {
                    if (DefaultBillsDatabase.SetDefaultPreset(thing.def.defName, name))
                        Messages.Message("WBD_DefaultSet".Translate(name, thing.def.LabelCap), MessageTypeDefOf.TaskCompletion, false);
                });
            }).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void OpenSettings()
        {
            var mod = LoadedModManager.GetMod<WBD_Mod>();
            if (mod != null)
                Find.WindowStack.Add(new Dialog_ModSettings(mod));
        }
    }
}
