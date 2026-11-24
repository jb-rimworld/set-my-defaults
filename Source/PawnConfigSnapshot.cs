using System;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>The independently toggleable pawn-default axes.</summary>
    [Flags]
    public enum PawnAxis
    {
        None = 0,
        Schedule = 1,
        Work = 2,
        Apparel = 4,
        Drug = 8,
        Food = 16,
        Reading = 32,
        Hostility = 64,
        Medical = 128,
        AllPolicies = Apparel | Drug | Food | Reading,
        All = Schedule | Work | AllPolicies | Hostility | Medical
    }

    /// <summary>
    /// A portable snapshot of a pawn's "managed" state: daily schedule, work priorities, the four
    /// assignment policies (apparel/drug/food/reading), and small player settings (hostility
    /// response, medical care, self-tend). The pawn analogue of <see cref="ConfigSnapshot"/>.
    ///
    /// Everything is stored portably (defNames / enums / def-based ThingFilters). Policies are the
    /// tricky part — they are save-specific objects, not defs — so <see cref="PolicyTemplate"/>
    /// captures their CONTENT and re-materialises them by label in the target colony on apply.
    /// </summary>
    public class PawnConfigSnapshot : IExposable
    {
        public TimetableTemplate timetable;
        public WorkPrioritiesTemplate work;
        public PolicyTemplate apparel;
        public PolicyTemplate drug;
        public PolicyTemplate food;
        public PolicyTemplate reading;
        public PawnMiscTemplate misc;

        public PawnConfigSnapshot() { }

        public bool IsEmpty =>
            (timetable == null || timetable.IsEmpty)
            && (work == null || work.IsEmpty)
            && (apparel == null || apparel.IsEmpty)
            && (drug == null || drug.IsEmpty)
            && (food == null || food.IsEmpty)
            && (reading == null || reading.IsEmpty)
            && misc == null;

        /// <summary>Capture everything we can read from a pawn. Returns null if nothing was captured.</summary>
        public static PawnConfigSnapshot Capture(Pawn pawn)
        {
            if (pawn == null)
                return null;
            var snap = new PawnConfigSnapshot
            {
                timetable = TimetableTemplate.Capture(pawn),
                work = WorkPrioritiesTemplate.Capture(pawn),
                apparel = PolicyTemplate.Capture(pawn, PolicyKind.Apparel),
                drug = PolicyTemplate.Capture(pawn, PolicyKind.Drug),
                food = PolicyTemplate.Capture(pawn, PolicyKind.Food),
                reading = PolicyTemplate.Capture(pawn, PolicyKind.Reading),
                misc = PawnMiscTemplate.Capture(pawn)
            };
            return snap.IsEmpty ? null : snap;
        }

        /// <summary>
        /// Apply the requested axes to a pawn. Each axis is independently gated so callers can apply
        /// a subset (auto-apply respects mod-settings toggles; the Work axis is dropped for pawns
        /// managed by Free Will before this is called). Returns the number of axes applied.
        /// </summary>
        public int ApplyTo(Pawn pawn, PawnAxis axes)
        {
            if (pawn == null)
                return 0;
            int applied = 0;

            if ((axes & PawnAxis.Schedule) != 0 && timetable != null && !timetable.IsEmpty)
            { timetable.ApplyTo(pawn); applied++; }

            if ((axes & PawnAxis.Work) != 0 && work != null && !work.IsEmpty)
            { work.ApplyTo(pawn); applied++; }

            if ((axes & PawnAxis.Apparel) != 0 && apparel != null && !apparel.IsEmpty)
            { apparel.ApplyTo(pawn); applied++; }

            if ((axes & PawnAxis.Drug) != 0 && drug != null && !drug.IsEmpty)
            { drug.ApplyTo(pawn); applied++; }

            if ((axes & PawnAxis.Food) != 0 && food != null && !food.IsEmpty)
            { food.ApplyTo(pawn); applied++; }

            if ((axes & PawnAxis.Reading) != 0 && reading != null && !reading.IsEmpty)
            { reading.ApplyTo(pawn); applied++; }

            if ((axes & PawnAxis.Hostility) != 0 && misc != null)
            { misc.ApplyHostility(pawn); applied++; }

            if ((axes & PawnAxis.Medical) != 0 && misc != null)
            { misc.ApplyMedical(pawn); applied++; }

            return applied;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref timetable, "timetable");
            Scribe_Deep.Look(ref work, "work");
            Scribe_Deep.Look(ref apparel, "apparel");
            Scribe_Deep.Look(ref drug, "drug");
            Scribe_Deep.Look(ref food, "food");
            Scribe_Deep.Look(ref reading, "reading");
            Scribe_Deep.Look(ref misc, "misc");
        }
    }
}
