using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    public enum PolicyKind { Apparel, Drug, Food, Reading }

    /// <summary>One drug's settings inside a drug policy (portable: drug is a def, rest are primitives).</summary>
    public class DrugEntryTemplate : IExposable
    {
        public string drug;
        public bool allowedForAddiction;
        public bool allowedForJoy;
        public bool allowScheduled;
        public float daysFrequency = 1f;
        public float onlyIfMoodBelow = 1f;
        public float onlyIfJoyBelow = 1f;
        public int takeToInventory;

        public DrugEntryTemplate() { }

        public static DrugEntryTemplate From(DrugPolicyEntry e)
        {
            if (e?.drug == null)
                return null;
            return new DrugEntryTemplate
            {
                drug = e.drug.defName,
                allowedForAddiction = e.allowedForAddiction,
                allowedForJoy = e.allowedForJoy,
                allowScheduled = e.allowScheduled,
                daysFrequency = e.daysFrequency,
                onlyIfMoodBelow = e.onlyIfMoodBelow,
                onlyIfJoyBelow = e.onlyIfJoyBelow,
                takeToInventory = e.takeToInventory
            };
        }

        public void ApplyTo(DrugPolicyEntry e)
        {
            if (e == null)
                return;
            e.allowedForAddiction = allowedForAddiction;
            e.allowedForJoy = allowedForJoy;
            e.allowScheduled = allowScheduled;
            e.daysFrequency = daysFrequency;
            e.onlyIfMoodBelow = onlyIfMoodBelow;
            e.onlyIfJoyBelow = onlyIfJoyBelow;
            e.takeToInventory = takeToInventory;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref drug, "drug");
            Scribe_Values.Look(ref allowedForAddiction, "addiction", false);
            Scribe_Values.Look(ref allowedForJoy, "joy", false);
            Scribe_Values.Look(ref allowScheduled, "scheduled", false);
            Scribe_Values.Look(ref daysFrequency, "freq", 1f);
            Scribe_Values.Look(ref onlyIfMoodBelow, "moodBelow", 1f);
            Scribe_Values.Look(ref onlyIfJoyBelow, "joyBelow", 1f);
            Scribe_Values.Look(ref takeToInventory, "takeToInv", 0);
        }
    }

    /// <summary>
    /// Colony-portable snapshot of ONE assignment policy (apparel/food/reading = ThingFilter-based;
    /// drug = a list of per-drug entries). Policies are save-specific objects (not defs), so we store
    /// the policy's LABEL plus its CONTENT and, on apply, match a policy by label in the destination
    /// colony — assigning the existing one as-is, or creating and populating a new one if none exists.
    /// One shared policy per label/colony, so re-applying to many colonists does not duplicate it.
    /// </summary>
    public class PolicyTemplate : IExposable
    {
        public PolicyKind kind;
        public string label;
        public ThingFilter filter;        // apparel/food: the filter; reading: the defFilter
        public ThingFilter effectFilter;  // reading only (special filters over BookEffects)
        public List<DrugEntryTemplate> drugEntries; // drug only

        public PolicyTemplate() { }

        public bool IsEmpty => label == null;

        public static PolicyTemplate Capture(Pawn pawn, PolicyKind kind)
        {
            if (pawn == null)
                return null;
            try
            {
                switch (kind)
                {
                    case PolicyKind.Apparel:
                        return CaptureFromPolicy(kind, pawn.outfits?.CurrentApparelPolicy);
                    case PolicyKind.Food:
                        return CaptureFromPolicy(kind, pawn.foodRestriction?.CurrentFoodPolicy);
                    case PolicyKind.Reading:
                        return CaptureFromPolicy(kind, pawn.reading?.CurrentPolicy);
                    case PolicyKind.Drug:
                        return CaptureFromPolicy(kind, pawn.drugs?.CurrentPolicy);
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Failed to capture " + kind + " policy: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// Capture a portable template directly from a live policy object (apparel/food/reading/drug),
        /// independent of any pawn — used to snapshot a colony's policy databases for the cross-save
        /// default-policies store. <paramref name="livePolicy"/> must be the matching policy type for
        /// <paramref name="kind"/> (ApparelPolicy / FoodPolicy / ReadingPolicy / DrugPolicy); a null
        /// policy (e.g. a mutant/ghoul with policies disabled) yields null.
        /// </summary>
        public static PolicyTemplate CaptureFromPolicy(PolicyKind kind, object livePolicy)
        {
            if (livePolicy == null)
                return null;
            try
            {
                switch (kind)
                {
                    case PolicyKind.Apparel:
                    {
                        var pol = (ApparelPolicy)livePolicy;
                        return FilterTemplate(kind, pol.label, pol.filter, null);
                    }
                    case PolicyKind.Food:
                    {
                        var pol = (FoodPolicy)livePolicy;
                        return FilterTemplate(kind, pol.label, pol.filter, null);
                    }
                    case PolicyKind.Reading:
                    {
                        var pol = (ReadingPolicy)livePolicy;
                        return FilterTemplate(kind, pol.label, pol.defFilter, pol.effectFilter);
                    }
                    case PolicyKind.Drug:
                    {
                        var pol = (DrugPolicy)livePolicy;
                        var t = new PolicyTemplate { kind = kind, label = pol.label, drugEntries = new List<DrugEntryTemplate>() };
                        for (int i = 0; i < pol.Count; i++)
                        {
                            var entry = DrugEntryTemplate.From(pol[i]);
                            if (entry != null)
                                t.drugEntries.Add(entry);
                        }
                        return t;
                    }
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Failed to capture " + kind + " policy: " + e.Message);
            }
            return null;
        }

        private static PolicyTemplate FilterTemplate(PolicyKind kind, string label, ThingFilter src, ThingFilter effectSrc)
        {
            var t = new PolicyTemplate { kind = kind, label = label };
            if (src != null)
            {
                t.filter = new ThingFilter();
                t.filter.CopyAllowancesFrom(src);
            }
            if (effectSrc != null)
            {
                t.effectFilter = new ThingFilter();
                t.effectFilter.CopyAllowancesFrom(effectSrc);
            }
            return t;
        }

        /// <summary>Resolve a live policy (by label, else create+populate) and assign it to the pawn.</summary>
        public void ApplyTo(Pawn pawn)
        {
            if (pawn == null || label == null || Current.Game == null)
                return;
            try
            {
                switch (kind)
                {
                    case PolicyKind.Apparel:
                    {
                        // Null current policy => mutant/ghoul with policies disabled; do not assign.
                        if (pawn.outfits == null || pawn.outfits.CurrentApparelPolicy == null)
                            return;
                        pawn.outfits.CurrentApparelPolicy = (ApparelPolicy)ApplyToDatabase(kind);
                        break;
                    }
                    case PolicyKind.Food:
                    {
                        if (pawn.foodRestriction == null || pawn.foodRestriction.CurrentFoodPolicy == null)
                            return;
                        pawn.foodRestriction.CurrentFoodPolicy = (FoodPolicy)ApplyToDatabase(kind);
                        break;
                    }
                    case PolicyKind.Reading:
                    {
                        if (pawn.reading == null || pawn.reading.CurrentPolicy == null)
                            return;
                        pawn.reading.CurrentPolicy = (ReadingPolicy)ApplyToDatabase(kind);
                        break;
                    }
                    case PolicyKind.Drug:
                    {
                        if (pawn.drugs == null || pawn.drugs.CurrentPolicy == null)
                            return;
                        pawn.drugs.CurrentPolicy = (DrugPolicy)ApplyToDatabase(kind);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Failed to apply " + kind + " policy '" + label + "': " + e.Message);
            }
        }

        /// <summary>
        /// Resolve this template's policy in the current colony's database BY LABEL, creating and
        /// populating a new one if none exists, and RETURN the live policy object (so a caller can then
        /// mark it the colony default via the database's SetDefault). An existing same-label policy is
        /// returned AS-IS — its content is deliberately not overwritten, matching the pawn-apply path,
        /// so a hand-tuned policy the player already has is never silently edited. Returns null when no
        /// game is loaded or the label is empty.
        /// </summary>
        public object ApplyToDatabase(PolicyKind k)
        {
            if (label == null || Current.Game == null)
                return null;
            try
            {
                switch (k)
                {
                    case PolicyKind.Apparel:
                    {
                        var db = Current.Game.outfitDatabase;
                        var pol = db.AllOutfits.FirstOrDefault(o => o.label == label);
                        if (pol == null)
                        {
                            pol = db.MakeNewOutfit();
                            pol.label = label;
                            CopyInto(pol.filter, filter);
                        }
                        return pol;
                    }
                    case PolicyKind.Food:
                    {
                        var db = Current.Game.foodRestrictionDatabase;
                        var pol = db.AllFoodRestrictions.FirstOrDefault(o => o.label == label);
                        if (pol == null)
                        {
                            pol = db.MakeNewFoodRestriction();
                            pol.label = label;
                            CopyInto(pol.filter, filter);
                        }
                        return pol;
                    }
                    case PolicyKind.Reading:
                    {
                        var db = Current.Game.readingPolicyDatabase;
                        var pol = db.AllReadingPolicies.FirstOrDefault(o => o.label == label);
                        if (pol == null)
                        {
                            pol = db.MakeNewReadingPolicy();
                            pol.label = label;
                            CopyInto(pol.defFilter, filter);
                            CopyInto(pol.effectFilter, effectFilter);
                        }
                        return pol;
                    }
                    case PolicyKind.Drug:
                    {
                        var db = Current.Game.drugPolicyDatabase;
                        var pol = db.AllPolicies.FirstOrDefault(o => o.label == label);
                        if (pol == null)
                        {
                            pol = db.MakeNewDrugPolicy();
                            pol.label = label;
                            PopulateDrugs(pol);
                        }
                        return pol;
                    }
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Failed to resolve " + k + " policy '" + label + "' in colony database: " + e.Message);
            }
            return null;
        }

        private static void CopyInto(ThingFilter target, ThingFilter source)
        {
            if (target != null && source != null)
                target.CopyAllowancesFrom(source);
        }

        private void PopulateDrugs(DrugPolicy pol)
        {
            if (drugEntries == null || pol == null)
                return;
            foreach (var t in drugEntries)
            {
                if (t?.drug == null)
                    continue;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(t.drug);
                if (def == null)
                    continue;
                // A fresh policy already has one entry per drug def; set ours by drug.
                t.ApplyTo(pol[def]);
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind", PolicyKind.Apparel);
            Scribe_Values.Look(ref label, "label");
            Scribe_Deep.Look(ref filter, "filter");
            Scribe_Deep.Look(ref effectFilter, "effectFilter");
            Scribe_Collections.Look(ref drugEntries, "drugEntries", LookMode.Deep);
        }
    }
}
