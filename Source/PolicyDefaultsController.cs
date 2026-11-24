using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Player-facing policy-default actions (parallel to <see cref="PawnDefaultsController"/>):
    ///  - capture the current colony's four policy databases into a <see cref="PolicyDefaultsSet"/>;
    ///  - apply a saved set into the current colony (recreate-by-label, then mark the saved default);
    ///  - the granular "make THIS existing colony policy the default" action, which simply calls the
    ///    vanilla database SetDefault (swapping the chosen policy into index 0, the slot every new pawn
    ///    and any tracker with a null current policy reads from).
    /// All mutations guard <c>Current.Game != null</c> and an idle Scribe so we never corrupt a save
    /// mid-load.
    /// </summary>
    public static class PolicyDefaultsController
    {
        public static readonly PolicyKind[] AllKinds =
            { PolicyKind.Apparel, PolicyKind.Drug, PolicyKind.Food, PolicyKind.Reading };

        public static bool GameReady =>
            Current.Game != null && Scribe.mode == LoadSaveMode.Inactive;

        // ---- capture (live DBs -> portable set) ----

        public static PolicyDefaultsSet CaptureCurrentColony()
        {
            if (!GameReady)
                return null;
            var set = new PolicyDefaultsSet();
            try
            {
                foreach (var kind in AllKinds)
                {
                    var list = set.ListFor(kind);
                    string defaultLabel = null;
                    foreach (var live in LivePolicies(kind))
                    {
                        var t = PolicyTemplate.CaptureFromPolicy(kind, live);
                        if (t != null && !t.IsEmpty)
                            list.Add(t);
                    }
                    var def = DefaultPolicy(kind);
                    if (def != null)
                        defaultLabel = PolicyLabel(def);
                    set.SetDefaultLabelFor(kind, defaultLabel);
                }
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to capture colony policy databases: " + e);
                return null;
            }
            set.NormalizeDefault();
            return set;
        }

        // ---- apply (portable set -> live DBs) ----

        public static bool ApplySetToColony(PolicyDefaultsSet set)
        {
            if (set == null || !GameReady)
                return false;
            try
            {
                foreach (var kind in AllKinds)
                {
                    var list = set.ListFor(kind);
                    if (list == null || list.Count == 0)
                        continue;

                    object defaultLive = null;
                    var defLabel = set.DefaultLabelFor(kind);
                    foreach (var t in list)
                    {
                        if (t == null || t.IsEmpty)
                            continue;
                        var live = t.ApplyToDatabase(kind);
                        if (live != null && defLabel != null && t.label == defLabel)
                            defaultLive = live;
                    }
                    if (defaultLive != null)
                        SetColonyDefault(kind, defaultLive);
                }
                return true;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Failed to apply policy defaults to colony: " + e);
                return false;
            }
        }

        // ---- granular per-kind default (vanilla SetDefault) ----

        /// <summary>
        /// Mark <paramref name="livePolicy"/> (a live policy object of the matching type for
        /// <paramref name="kind"/>) as the colony default by calling the vanilla database SetDefault,
        /// which swaps it into index 0 so new pawns inherit it.
        /// </summary>
        public static bool SetColonyDefault(PolicyKind kind, object livePolicy)
        {
            if (!GameReady || livePolicy == null)
                return false;
            try
            {
                switch (kind)
                {
                    case PolicyKind.Apparel:
                        Current.Game.outfitDatabase.SetDefault((ApparelPolicy)livePolicy);
                        return true;
                    case PolicyKind.Drug:
                        Current.Game.drugPolicyDatabase.SetDefault((DrugPolicy)livePolicy);
                        return true;
                    case PolicyKind.Food:
                        Current.Game.foodRestrictionDatabase.SetDefault((FoodPolicy)livePolicy);
                        return true;
                    case PolicyKind.Reading:
                        Current.Game.readingPolicyDatabase.SetDefault((ReadingPolicy)livePolicy);
                        return true;
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Failed to set colony default " + kind + " policy: " + e.Message);
            }
            return false;
        }

        /// <summary>
        /// Move <paramref name="p"/> to the colony's current default <paramref name="kind"/> policy if it is still on
        /// <paramref name="oldDefault"/> (the default before a policy set was applied). Pawns whose policies are
        /// switched off (mutants) are skipped. Returns true when the pawn was moved.
        /// </summary>
        public static bool ReassignIfOnOldDefault(Pawn p, PolicyKind kind, object oldDefault)
        {
            var now = DefaultPolicy(kind);
            if (p == null || oldDefault == null || now == null || ReferenceEquals(now, oldDefault))
                return false;
            try
            {
                switch (kind)
                {
                    case PolicyKind.Apparel:
                        if (p.outfits?.CurrentApparelPolicy == null || !ReferenceEquals(p.outfits.CurrentApparelPolicy, oldDefault)) return false;
                        p.outfits.CurrentApparelPolicy = (ApparelPolicy)now;
                        return true;
                    case PolicyKind.Drug:
                        if (p.drugs?.CurrentPolicy == null || !ReferenceEquals(p.drugs.CurrentPolicy, oldDefault)) return false;
                        p.drugs.CurrentPolicy = (DrugPolicy)now;
                        return true;
                    case PolicyKind.Food:
                        if (p.foodRestriction?.CurrentFoodPolicy == null || !ReferenceEquals(p.foodRestriction.CurrentFoodPolicy, oldDefault)) return false;
                        p.foodRestriction.CurrentFoodPolicy = (FoodPolicy)now;
                        return true;
                    case PolicyKind.Reading:
                        if (p.reading?.CurrentPolicy == null || !ReferenceEquals(p.reading.CurrentPolicy, oldDefault)) return false;
                        p.reading.CurrentPolicy = (ReadingPolicy)now;
                        return true;
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not move " + p.LabelShort + " to the new default " + kind + " policy: " + e.Message);
            }
            return false;
        }

        // ---- live-database helpers (used by the settings UI) ----

        public static IEnumerable<object> LivePolicies(PolicyKind kind)
        {
            if (Current.Game == null)
                yield break;
            switch (kind)
            {
                case PolicyKind.Apparel:
                    foreach (var p in Current.Game.outfitDatabase.AllOutfits) yield return p;
                    break;
                case PolicyKind.Drug:
                    foreach (var p in Current.Game.drugPolicyDatabase.AllPolicies) yield return p;
                    break;
                case PolicyKind.Food:
                    foreach (var p in Current.Game.foodRestrictionDatabase.AllFoodRestrictions) yield return p;
                    break;
                case PolicyKind.Reading:
                    foreach (var p in Current.Game.readingPolicyDatabase.AllReadingPolicies) yield return p;
                    break;
            }
        }

        public static object DefaultPolicy(PolicyKind kind)
        {
            if (Current.Game == null)
                return null;
            switch (kind)
            {
                case PolicyKind.Apparel: return Current.Game.outfitDatabase.DefaultOutfit();
                case PolicyKind.Drug: return Current.Game.drugPolicyDatabase.DefaultDrugPolicy();
                case PolicyKind.Food: return Current.Game.foodRestrictionDatabase.DefaultFoodRestriction();
                case PolicyKind.Reading: return Current.Game.readingPolicyDatabase.DefaultReadingPolicy();
            }
            return null;
        }

        public static string PolicyLabel(object policy)
        {
            switch (policy)
            {
                case ApparelPolicy a: return a.label;
                case DrugPolicy d: return d.label;
                case FoodPolicy f: return f.label;
                case ReadingPolicy r: return r.label;
            }
            return null;
        }

        /// <summary>Open the matching vanilla manage dialog so the player can create/edit policies.</summary>
        public static void OpenManageDialog(PolicyKind kind)
        {
            if (Current.Game == null)
                return;
            try
            {
                switch (kind)
                {
                    case PolicyKind.Apparel:
                        Find.WindowStack.Add(new Dialog_ManageApparelPolicies(null));
                        break;
                    case PolicyKind.Drug:
                        Find.WindowStack.Add(new Dialog_ManageDrugPolicies(null));
                        break;
                    case PolicyKind.Food:
                        Find.WindowStack.Add(new Dialog_ManageFoodPolicies(null));
                        break;
                    case PolicyKind.Reading:
                        Find.WindowStack.Add(new Dialog_ManageReadingPolicies(null));
                        break;
                }
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not open the manage dialog for " + kind + ": " + e.Message);
            }
        }

        public static string KindLabel(PolicyKind kind)
        {
            switch (kind)
            {
                case PolicyKind.Apparel: return "WBD_PolicyKindApparel".Translate();
                case PolicyKind.Drug: return "WBD_PolicyKindDrug".Translate();
                case PolicyKind.Food: return "WBD_PolicyKindFood".Translate();
                case PolicyKind.Reading: return "WBD_PolicyKindReading".Translate();
            }
            return kind.ToString();
        }
    }
}
