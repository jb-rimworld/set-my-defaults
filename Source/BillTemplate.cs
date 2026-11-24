using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// A colony-portable snapshot of a single production bill.
    /// Stores only defNames + value settings + the ingredient filter (which is itself
    /// purely def-based) + the colony-portable "allowed workers" category. It deliberately
    /// does NOT store the per-pawn restriction or a specific stockpile target, because those
    /// reference objects that only exist in one save game.
    /// This keeps the on-disk default valid across every colony and avoids dangling
    /// cross-references when the file is loaded outside of a running game.
    /// </summary>
    public class BillTemplate : IExposable
    {
        public string recipeDefName;
        public bool suspended;
        public bool paused;
        public string repeatModeDefName;
        public int repeatCount;
        public int targetCount;
        public bool pauseWhenSatisfied;
        public int unpauseWhenYouHave;
        public float ingredientSearchRadius = 999f;
        public IntRange allowedSkillRange = new IntRange(0, 20);
        public QualityRange qualityRange = QualityRange.All;
        public FloatRange hpRange = FloatRange.ZeroToOne;
        public bool limitToAllowedStuff;
        public bool includeEquipped;
        public bool includeTainted;
        public string storeModeDefName;
        // "Allowed workers" category. These are pure booleans (unlike a per-pawn restriction),
        // so they are fully portable across colonies.
        public bool slavesOnly;
        public bool mechsOnly;
        public bool nonMechsOnly;
        public ThingFilter ingredientFilter;

        public BillTemplate() { }

        /// <summary>Build a template from a live bill (sanitising non-portable settings).</summary>
        public static BillTemplate FromBill(Bill_Production b)
        {
            var t = new BillTemplate
            {
                recipeDefName = b.recipe?.defName,
                suspended = b.suspended,
                paused = b.paused,
                repeatModeDefName = b.repeatMode?.defName,
                repeatCount = b.repeatCount,
                targetCount = b.targetCount,
                pauseWhenSatisfied = b.pauseWhenSatisfied,
                unpauseWhenYouHave = b.unpauseWhenYouHave,
                ingredientSearchRadius = b.ingredientSearchRadius,
                allowedSkillRange = b.allowedSkillRange,
                qualityRange = b.qualityRange,
                hpRange = b.hpRange,
                limitToAllowedStuff = b.limitToAllowedStuff,
                includeEquipped = b.includeEquipped,
                includeTainted = b.includeTainted,
                slavesOnly = b.SlavesOnly,
                mechsOnly = b.MechsOnly,
                nonMechsOnly = b.NonMechsOnly
            };

            // Storage mode: drop a "specific stockpile" target down to "best stockpile",
            // because that exact stockpile won't exist in another colony.
            var mode = b.GetStoreMode();
            if (mode == BillStoreModeDefOf.SpecificStockpile)
                mode = BillStoreModeDefOf.BestStockpile;
            t.storeModeDefName = mode?.defName;

            if (b.ingredientFilter != null)
            {
                t.ingredientFilter = new ThingFilter();
                t.ingredientFilter.CopyAllowancesFrom(b.ingredientFilter);
            }

            return t;
        }

        /// <summary>True if the recipe this template refers to still exists in the loaded game.</summary>
        public bool IsValid => recipeDefName != null
                               && DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName) != null;

        /// <summary>
        /// True if the given workbench can actually run this template's recipe right now.
        /// Requires AvailableNow so we never create a bill the player could not add through the
        /// vanilla "Add bill" menu (which filters by AvailableNow). Recipes locked behind
        /// research/DLC are simply skipped; they will be included again on benches built later,
        /// once unlocked, because defaults are re-read per bench.
        /// </summary>
        public bool AppliesTo(ThingDef workbenchDef)
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName);
            return recipe != null
                   && recipe.AvailableNow
                   && workbenchDef?.AllRecipes != null
                   && workbenchDef.AllRecipes.Contains(recipe);
        }

        /// <summary>Instantiate a fresh bill from this template, or null if the recipe is gone.</summary>
        public Bill_Production CreateBill()
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName);
            if (recipe == null)
                return null;
            if (!(recipe.MakeNewBill() is Bill_Production bill))
                return null;

            bill.suspended = suspended;
            bill.paused = paused;

            var rm = repeatModeDefName != null
                ? DefDatabase<BillRepeatModeDef>.GetNamedSilentFail(repeatModeDefName)
                : null;
            if (rm != null)
                bill.repeatMode = rm;

            bill.repeatCount = repeatCount;
            bill.targetCount = targetCount;
            bill.pauseWhenSatisfied = pauseWhenSatisfied;
            bill.unpauseWhenYouHave = unpauseWhenYouHave;
            bill.ingredientSearchRadius = ingredientSearchRadius;
            bill.allowedSkillRange = allowedSkillRange;
            bill.qualityRange = qualityRange;
            bill.hpRange = hpRange;
            bill.limitToAllowedStuff = limitToAllowedStuff;
            bill.includeEquipped = includeEquipped;
            bill.includeTainted = includeTainted;

            var mode = storeModeDefName != null
                ? DefDatabase<BillStoreModeDef>.GetNamedSilentFail(storeModeDefName)
                : null;
            if (mode != null)
                bill.SetStoreMode(mode);

            if (ingredientFilter != null && bill.ingredientFilter != null)
                bill.ingredientFilter.CopyAllowancesFrom(ingredientFilter);

            // "Allowed workers" category (a fresh bill already defaults to "anyone").
            if (slavesOnly)
                bill.SetAnySlaveRestriction();
            else if (mechsOnly)
                bill.SetAnyMechRestriction();
            else if (nonMechsOnly)
                bill.SetAnyNonMechRestriction();

            return bill;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref recipeDefName, "recipe");
            Scribe_Values.Look(ref suspended, "suspended", false);
            Scribe_Values.Look(ref paused, "paused", false);
            Scribe_Values.Look(ref repeatModeDefName, "repeatMode");
            Scribe_Values.Look(ref repeatCount, "repeatCount", 0);
            Scribe_Values.Look(ref targetCount, "targetCount", 0);
            Scribe_Values.Look(ref pauseWhenSatisfied, "pauseWhenSatisfied", false);
            Scribe_Values.Look(ref unpauseWhenYouHave, "unpauseWhenYouHave", 0);
            Scribe_Values.Look(ref ingredientSearchRadius, "ingredientSearchRadius", 999f);
            Scribe_Values.Look(ref allowedSkillRange, "allowedSkillRange", new IntRange(0, 20));
            Scribe_Values.Look(ref qualityRange, "qualityRange", QualityRange.All);
            Scribe_Values.Look(ref hpRange, "hpRange", FloatRange.ZeroToOne);
            Scribe_Values.Look(ref limitToAllowedStuff, "limitToAllowedStuff", false);
            Scribe_Values.Look(ref includeEquipped, "includeEquipped", false);
            Scribe_Values.Look(ref includeTainted, "includeTainted", false);
            Scribe_Values.Look(ref slavesOnly, "slavesOnly", false);
            Scribe_Values.Look(ref mechsOnly, "mechsOnly", false);
            Scribe_Values.Look(ref nonMechsOnly, "nonMechsOnly", false);
            Scribe_Values.Look(ref storeModeDefName, "storeMode");
            Scribe_Deep.Look(ref ingredientFilter, "ingredientFilter");
        }

        /// <summary>Capture every production bill currently on a workbench.</summary>
        public static List<BillTemplate> Capture(BillStack stack)
        {
            var result = new List<BillTemplate>();
            if (stack == null)
                return result;
            foreach (var bill in stack.Bills)
            {
                if (bill is Bill_Production prod && prod.recipe != null)
                    result.Add(FromBill(prod));
            }
            return result;
        }
    }
}
