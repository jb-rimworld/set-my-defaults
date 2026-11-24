using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>Player-facing pawn-default actions, shared by the pawn gizmo (and the settings overview).</summary>
    public static class PawnDefaultsController
    {
        public static bool IsTarget(Pawn p)
        {
            return p != null
                   && p.RaceProps != null && p.RaceProps.Humanlike
                   && p.Faction == Faction.OfPlayerSilentFail
                   && p.IsColonist;
        }

        public static void OpenMenu(Pawn pawn)
        {
            if (pawn == null)
                return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("WBD_PawnSaveAsPreset".Translate(), () => PromptSave(pawn))
            };

            var presets = PawnDefaultsStore.GetPresets();
            if (presets != null && !presets.IsEmpty)
            {
                int targetCount = SelectedTargets(pawn).Count;
                foreach (var preset in presets.presets)
                {
                    var name = preset.name;
                    var label = presets.IsDefault(name)
                        ? "WBD_ApplyNamedDefault".Translate(name)
                        : "WBD_ApplyNamed".Translate(name);
                    if (targetCount > 1)
                        label += " (" + "WBD_PawnApplyToN".Translate(targetCount) + ")";
                    options.Add(new FloatMenuOption(label, () => ApplyPreset(pawn, name)));
                }

                if (presets.presets.Count > 1)
                    options.Add(new FloatMenuOption("WBD_SetDefaultPreset".Translate(), () => OpenSetDefaultMenu()));
            }
            options.Add(new FloatMenuOption("WBD_OpenLibrary".Translate(), WBD_Mod.OpenSettingsPawnTab));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Selected player colonists to act on (falls back to just the clicked pawn).</summary>
        private static List<Pawn> SelectedTargets(Pawn fallback)
        {
            var selected = Find.Selector?.SelectedPawns?.Where(IsTarget).ToList();
            if (selected != null && selected.Count > 0)
                return selected;
            return IsTarget(fallback) ? new List<Pawn> { fallback } : new List<Pawn>();
        }

        private static void PromptSave(Pawn pawn)
        {
            var presets = PawnDefaultsStore.GetPresets();
            var suggested = (presets == null || presets.IsEmpty) ? PawnPresets.DefaultName : "";
            Find.WindowStack.Add(new WBD_Dialog_NamePreset(
                "WBD_PawnSavePromptTitle".Translate(),
                suggested,
                name => SaveAsPreset(pawn, name)));
        }

        public static void SaveAsPreset(Pawn pawn, string name)
        {
            if (pawn == null || name.NullOrEmpty())
                return;
            var config = PawnConfigSnapshot.Capture(pawn);
            if (config == null)
            {
                Messages.Message("WBD_PawnNothingToSave".Translate(pawn.LabelShortCap), MessageTypeDefOf.RejectInput, false);
                return;
            }
            bool makeDefault = !PawnDefaultsStore.HasPresets(); // first preset becomes the default
            if (!PawnDefaultsStore.SavePreset(name, config, makeDefault))
            {
                Messages.Message(DefaultBillsController.SaveFailedText(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Messages.Message("WBD_PawnPresetSaved".Translate(name), MessageTypeDefOf.TaskCompletion, false);
        }

        public static void ApplyPreset(Pawn pawn, string name)
        {
            var preset = PawnDefaultsStore.GetPresets()?.Get(name);
            if (preset?.config == null || preset.config.IsEmpty)
            {
                Messages.Message("WBD_PawnNoPreset".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            var targets = SelectedTargets(pawn);
            if (targets.Count == 0)
                return;

            // Manual apply = the full preset (every axis it stored), since the player asked explicitly.
            int applied = 0;
            foreach (var t in targets)
            {
                preset.config.ApplyTo(t, PawnAxis.All);
                applied++;
            }
            Messages.Message("WBD_PawnPresetApplied".Translate(name, applied), MessageTypeDefOf.TaskCompletion, false);
        }

        private static void OpenSetDefaultMenu()
        {
            var presets = PawnDefaultsStore.GetPresets();
            if (presets == null || presets.IsEmpty)
                return;
            var options = presets.presets.Select(p =>
            {
                var name = p.name;
                var label = presets.IsDefault(name) ? "WBD_PresetIsDefault".Translate(name) : (TaggedString)name;
                return new FloatMenuOption(label, () =>
                {
                    if (PawnDefaultsStore.SetDefaultPreset(name))
                        Messages.Message("WBD_PawnDefaultSet".Translate(name), MessageTypeDefOf.TaskCompletion, false);
                });
            }).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
