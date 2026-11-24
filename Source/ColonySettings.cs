using System;
using System.Collections.Generic;
using BetterWorkbenchDefaults.Lib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Cross-save colony settings (M8): the per-save play settings players set again in every new colony.
    /// Medical care default per pawn category (vanilla "Default medical care" dialog), auto home area, auto rebuild,
    /// manual work priorities. Values and Scribe names mirror <see cref="PlaySettings"/> (verified against 1.7).
    /// </summary>
    public class ColonySettingsTemplate : IExposable
    {
        public bool autoHomeArea = true;
        public bool autoRebuild;
        public bool useWorkPriorities;
        public MedicalCareCategory careColonist = MedicalCareCategory.Best;
        public MedicalCareCategory carePrisoner = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careSlave = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careTamedAnimal = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careFriendly = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careNeutral = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careHostile = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careNoFaction = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careWildlife = MedicalCareCategory.HerbalOrWorse;
        public MedicalCareCategory careEntities = MedicalCareCategory.NoMeds;
        public MedicalCareCategory careGhouls = MedicalCareCategory.NoMeds;

        public static ColonySettingsTemplate Capture(PlaySettings ps)
        {
            if (ps == null)
                return null;
            return new ColonySettingsTemplate
            {
                autoHomeArea = ps.autoHomeArea,
                autoRebuild = ps.autoRebuild,
                useWorkPriorities = ps.useWorkPriorities,
                careColonist = ps.defaultCareForColonist,
                carePrisoner = ps.defaultCareForPrisoner,
                careSlave = ps.defaultCareForSlave,
                careTamedAnimal = ps.defaultCareForTamedAnimal,
                careFriendly = ps.defaultCareForFriendlyFaction,
                careNeutral = ps.defaultCareForNeutralFaction,
                careHostile = ps.defaultCareForHostileFaction,
                careNoFaction = ps.defaultCareForNoFaction,
                careWildlife = ps.defaultCareForWildlife,
                careEntities = ps.defaultCareForEntities,
                careGhouls = ps.defaultCareForGhouls
            };
        }

        public void ApplyTo(PlaySettings ps)
        {
            if (ps == null)
                return;
            ps.autoHomeArea = autoHomeArea;
            ps.autoRebuild = autoRebuild;
            ps.useWorkPriorities = useWorkPriorities;
            ps.defaultCareForColonist = careColonist;
            ps.defaultCareForPrisoner = carePrisoner;
            ps.defaultCareForSlave = careSlave;
            ps.defaultCareForTamedAnimal = careTamedAnimal;
            ps.defaultCareForFriendlyFaction = careFriendly;
            ps.defaultCareForNeutralFaction = careNeutral;
            ps.defaultCareForHostileFaction = careHostile;
            ps.defaultCareForNoFaction = careNoFaction;
            ps.defaultCareForWildlife = careWildlife;
            if (ModsConfig.AnomalyActive)
            {
                ps.defaultCareForEntities = careEntities;
                ps.defaultCareForGhouls = careGhouls;
            }
        }

        /// <summary>
        /// The default this template gives <paramref name="p"/>: the same category choice as vanilla
        /// Pawn_PlayerSettings.ResetMedicalCare (1.7), so "was this pawn still on the default?" can be answered.
        /// </summary>
        public MedicalCareCategory DefaultFor(Pawn p)
        {
            if (p.Faction == Faction.OfPlayerSilentFail)
            {
                if (ModsConfig.AnomalyActive && p.IsGhoul) return careGhouls;
                if (ModsConfig.AnomalyActive && p.IsEntity) return careEntities;
                if (!p.RaceProps.Animal) return p.IsSlave ? careSlave : careColonist;
                return careTamedAnimal;
            }
            if (ModsConfig.AnomalyActive && p.IsEntity) return careEntities;
            if (p.IsPrisoner) return carePrisoner;
            if (p.Faction == null && p.RaceProps.Animal) return careWildlife;
            if (p.Faction != null)
            {
                switch (p.Faction.RelationWith(Faction.OfPlayer).kind)
                {
                    case FactionRelationKind.Ally: return careFriendly;
                    case FactionRelationKind.Neutral: return careNeutral;
                    default: return careHostile;
                }
            }
            return careNoFaction;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref autoHomeArea, "autoHomeArea", true);
            Scribe_Values.Look(ref autoRebuild, "autoRebuild", false);
            Scribe_Values.Look(ref useWorkPriorities, "useWorkPriorities", false);
            Scribe_Values.Look(ref careColonist, "careColonist", MedicalCareCategory.Best);
            Scribe_Values.Look(ref carePrisoner, "carePrisoner", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careSlave, "careSlave", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careTamedAnimal, "careTamedAnimal", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careFriendly, "careFriendly", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careNeutral, "careNeutral", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careHostile, "careHostile", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careNoFaction, "careNoFaction", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careWildlife, "careWildlife", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref careEntities, "careEntities", MedicalCareCategory.NoMeds);
            Scribe_Values.Look(ref careGhouls, "careGhouls", MedicalCareCategory.NoMeds);
        }
    }

    /// <summary>The colony-settings section of the library (one block).</summary>
    public static class ColonySettingsStore
    {
        private static int _rev = -1;
        private static ColonySettingsTemplate _cache;

        public static StoredBlock GetStored() => SmdLibraryStore.Content.colony;

        public static ColonySettingsTemplate Get()
        {
            int rev = SmdLibraryStore.Revision;
            if (rev == _rev)
                return _cache;
            var stored = GetStored();
            ColonySettingsTemplate t = null;
            if (stored != null)
            {
                t = new ColonySettingsTemplate();
                if (!SmdScribe.Read(stored.config, t.ExposeData))
                {
                    if (SmdScribe.Busy)
                        return null;
                    t = null;
                }
            }
            _cache = t;
            _rev = rev;
            return t;
        }

        public static bool Has() => GetStored() != null;

        /// <summary>Readable lines of the saved colony settings (settings page).</summary>
        public static System.Collections.Generic.List<string> SummaryLines()
        {
            var l = new System.Collections.Generic.List<string>();
            var t = Get();
            if (t == null) return l;
            string yn(bool b) => (b ? "Yes" : "No").Translate().ToString();
            void care(string group, MedicalCareCategory c) => l.Add("WBD_ColSumCare".Translate(("WBD_CareGroup_" + group).Translate(), c.GetLabel()));
            care("Colonist", t.careColonist); care("Prisoner", t.carePrisoner); care("Slave", t.careSlave); care("TamedAnimal", t.careTamedAnimal);
            care("Friendly", t.careFriendly); care("Neutral", t.careNeutral); care("Hostile", t.careHostile); care("NoFaction", t.careNoFaction);
            care("Wildlife", t.careWildlife);
            if (ModsConfig.AnomalyActive) { care("Entities", t.careEntities); care("Ghouls", t.careGhouls); }
            l.Add("WBD_ColSumHome".Translate(yn(t.autoHomeArea)));
            l.Add("WBD_ColSumRebuild".Translate(yn(t.autoRebuild)));
            l.Add("WBD_ColSumPriorities".Translate(yn(t.useWorkPriorities)));
            return l;
        }

        /// <summary>Save the running game's play settings as the cross-save colony settings.</summary>
        public static bool SaveCurrentGame()
        {
            var block = CaptureCurrentGameBlock();
            if (block == null)
                return false;
            return SmdLibraryStore.Mutate(lib =>
            {
                lib.content.colony = block;
                return true;
            });
        }

        /// <summary>The running game's play settings as a stored block (for profiles). Null without a game.</summary>
        public static StoredBlock CaptureCurrentGameBlock()
        {
            if (Current.Game == null)
                return null;
            var t = ColonySettingsTemplate.Capture(Find.PlaySettings);
            var xml = SmdScribe.Write(t.ExposeData);
            return xml == null ? null : new StoredBlock { config = xml, savedAt = DateTime.UtcNow.ToString("yyyy-MM-dd") };
        }

        public static void Clear() =>
            SmdLibraryStore.Mutate(lib =>
            {
                if (lib.content.colony == null)
                    return false;
                lib.content.colony = null;
                return true;
            });

        /// <summary>
        /// Apply the saved colony settings to the running game. Pawns that were still on the old category default get
        /// the new one (vanilla ResetMedicalCare); pawns listed in <paramref name="keepMedical"/> are left alone.
        /// Returns false when nothing is saved or no game runs.
        /// </summary>
        public static bool ApplyToCurrentGame(HashSet<int> keepMedical = null)
        {
            if (Current.Game == null || Scribe.mode != LoadSaveMode.Inactive)
                return false;
            var t = Get();
            if (t == null)
                return false;
            var ps = Find.PlaySettings;
            var before = ColonySettingsTemplate.Capture(ps);
            t.ApplyTo(ps);
            try
            {
                bool prioritiesChanged = before.useWorkPriorities != ps.useWorkPriorities;
                foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (p == null)
                        continue;
                    if (prioritiesChanged && p.Faction == Faction.OfPlayerSilentFail && p.workSettings != null)
                        p.workSettings.Notify_UseWorkPrioritiesChanged(); // what the vanilla Work tab checkbox does
                    if (p.playerSettings == null || (keepMedical != null && keepMedical.Contains(p.thingIDNumber)))
                        continue;
                    if (p.playerSettings.medCare == before.DefaultFor(p))
                        p.playerSettings.ResetMedicalCare();
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Colony settings applied, but updating existing pawns failed: " + e.Message);
            }
            return true;
        }
    }

    /// <summary>
    /// What happens once when a NEW game starts (M8, fixes B6). Runs in GameComponent.StartedNewGame: after the map
    /// and the starting colonists exist, before the first tick. Loading a save never runs it.
    /// </summary>
    public static class ColonyDefaults
    {
        public static void ApplyAtNewGame(PawnDefaultsGameComponent comp)
        {
            var s = WBD_Mod.Settings;
            if (s == null || Current.Game == null)
                return;

            // Which starting colonists got the colonist preset (its medical/policy axes win over the colony defaults).
            var preset = PawnDefaultsStore.GetDefaultConfig();
            var byPreset = new HashSet<int>();
            if (comp != null && preset != null && s.autoApplyToNewPawns && SmdSections.On(SmdSections.Pawns))
                foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                    if (p != null && comp.CoreApplied(p.thingIDNumber))
                        byPreset.Add(p.thingIDNumber);

            if (s.colonyApplyOnNewGame && SmdSections.On(SmdSections.Colony) && ColonySettingsStore.Has())
            {
                var keep = (preset?.misc != null && s.pawnApplyMedical) ? byPreset : null;
                if (ColonySettingsStore.ApplyToCurrentGame(keep))
                    WBD_Log.Msg("Colony settings applied to the new game.");
            }

            if (s.policyAutoApplyOnNewGame && SmdSections.On(SmdSections.Policies) && PolicyDefaultsStore.HasSet())
            {
                var oldDefaults = new Dictionary<PolicyKind, object>();
                foreach (var kind in PolicyDefaultsController.AllKinds)
                    oldDefaults[kind] = PolicyDefaultsController.DefaultPolicy(kind);
                if (PolicyDefaultsStore.ApplyToCurrentColony())
                {
                    int moved = 0;
                    foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                    {
                        if (p == null || p.Faction != Faction.OfPlayerSilentFail || p.RaceProps == null || !p.RaceProps.Humanlike)
                            continue;
                        foreach (var kind in PolicyDefaultsController.AllKinds)
                        {
                            if (byPreset.Contains(p.thingIDNumber) && PresetSetsPolicy(preset, kind, s))
                                continue;
                            if (PolicyDefaultsController.ReassignIfOnOldDefault(p, kind, oldDefaults[kind]))
                                moved++;
                        }
                    }
                    WBD_Log.Msg("Saved policies applied to the new game (" + moved + " starting-pawn policy assignments updated).");
                }
            }
        }

        private static bool PresetSetsPolicy(PawnConfigSnapshot preset, PolicyKind kind, WBD_Settings s)
        {
            if (preset == null)
                return false;
            switch (kind)
            {
                case PolicyKind.Apparel: return s.pawnApplyApparel && preset.apparel != null && !preset.apparel.IsEmpty;
                case PolicyKind.Drug: return s.pawnApplyDrug && preset.drug != null && !preset.drug.IsEmpty;
                case PolicyKind.Food: return s.pawnApplyFood && preset.food != null && !preset.food.IsEmpty;
                case PolicyKind.Reading: return s.pawnApplyReading && preset.reading != null && !preset.reading.IsEmpty;
            }
            return false;
        }
    }
}
