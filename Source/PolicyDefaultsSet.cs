using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The cross-save "default policies" set: a portable snapshot of a colony's four policy databases
    /// (apparel/outfit, drug, food, reading). For each kind it stores the list of policies (as
    /// <see cref="PolicyTemplate"/>, which already captures label + portable content) plus the LABEL of
    /// the one that is the colony default (the policy at database index 0, which brand-new pawns inherit).
    ///
    /// Parallel in spirit to <see cref="PawnPresets"/> but there is exactly ONE global set (not a list of
    /// named presets), since "the default policies" is a single concept the player saves and re-applies.
    /// </summary>
    public class PolicyDefaultsSet : IExposable
    {
        public List<PolicyTemplate> apparel = new List<PolicyTemplate>();
        public List<PolicyTemplate> drug = new List<PolicyTemplate>();
        public List<PolicyTemplate> food = new List<PolicyTemplate>();
        public List<PolicyTemplate> reading = new List<PolicyTemplate>();

        public string apparelDefaultLabel;
        public string drugDefaultLabel;
        public string foodDefaultLabel;
        public string readingDefaultLabel;

        public bool IsEmpty =>
            (apparel == null || apparel.Count == 0)
            && (drug == null || drug.Count == 0)
            && (food == null || food.Count == 0)
            && (reading == null || reading.Count == 0);

        public List<PolicyTemplate> ListFor(PolicyKind kind)
        {
            switch (kind)
            {
                case PolicyKind.Apparel: return apparel;
                case PolicyKind.Drug: return drug;
                case PolicyKind.Food: return food;
                case PolicyKind.Reading: return reading;
            }
            return null;
        }

        public string DefaultLabelFor(PolicyKind kind)
        {
            switch (kind)
            {
                case PolicyKind.Apparel: return apparelDefaultLabel;
                case PolicyKind.Drug: return drugDefaultLabel;
                case PolicyKind.Food: return foodDefaultLabel;
                case PolicyKind.Reading: return readingDefaultLabel;
            }
            return null;
        }

        public void SetDefaultLabelFor(PolicyKind kind, string label)
        {
            switch (kind)
            {
                case PolicyKind.Apparel: apparelDefaultLabel = label; break;
                case PolicyKind.Drug: drugDefaultLabel = label; break;
                case PolicyKind.Food: foodDefaultLabel = label; break;
                case PolicyKind.Reading: readingDefaultLabel = label; break;
            }
        }

        /// <summary>Drop empty templates and ensure each kind's default label points at a real entry.</summary>
        public void NormalizeDefault()
        {
            foreach (PolicyKind kind in new[] { PolicyKind.Apparel, PolicyKind.Drug, PolicyKind.Food, PolicyKind.Reading })
            {
                var list = ListFor(kind);
                list?.RemoveAll(t => t == null || t.IsEmpty);
                var def = DefaultLabelFor(kind);
                if (list == null || list.Count == 0)
                {
                    SetDefaultLabelFor(kind, null);
                }
                else if (def == null || list.All(t => t.label != def))
                {
                    SetDefaultLabelFor(kind, list[0].label);
                }
            }
        }

        public PolicyDefaultsSet Clone()
        {
            return new PolicyDefaultsSet
            {
                apparel = (apparel ?? new List<PolicyTemplate>()).Where(t => t != null).ToList(),
                drug = (drug ?? new List<PolicyTemplate>()).Where(t => t != null).ToList(),
                food = (food ?? new List<PolicyTemplate>()).Where(t => t != null).ToList(),
                reading = (reading ?? new List<PolicyTemplate>()).Where(t => t != null).ToList(),
                apparelDefaultLabel = apparelDefaultLabel,
                drugDefaultLabel = drugDefaultLabel,
                foodDefaultLabel = foodDefaultLabel,
                readingDefaultLabel = readingDefaultLabel
            };
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref apparel, "apparel", LookMode.Deep);
            Scribe_Collections.Look(ref drug, "drug", LookMode.Deep);
            Scribe_Collections.Look(ref food, "food", LookMode.Deep);
            Scribe_Collections.Look(ref reading, "reading", LookMode.Deep);
            Scribe_Values.Look(ref apparelDefaultLabel, "apparelDefault");
            Scribe_Values.Look(ref drugDefaultLabel, "drugDefault");
            Scribe_Values.Look(ref foodDefaultLabel, "foodDefault");
            Scribe_Values.Look(ref readingDefaultLabel, "readingDefault");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                apparel = apparel ?? new List<PolicyTemplate>();
                drug = drug ?? new List<PolicyTemplate>();
                food = food ?? new List<PolicyTemplate>();
                reading = reading ?? new List<PolicyTemplate>();
            }
        }
    }
}
