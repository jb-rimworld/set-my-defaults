using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Colony-portable snapshot of a pawn's daily schedule (the 24 hourly time assignments).
    /// Stores only <see cref="TimeAssignmentDef"/> defNames, which are global defs identical in
    /// every save, so it carries across colonies with no save-specific references.
    /// </summary>
    public class TimetableTemplate : IExposable
    {
        // Exactly 24 entries (hours 0..23), each a TimeAssignmentDef defName.
        public List<string> assignments;

        public TimetableTemplate() { }

        public bool IsEmpty => assignments == null || assignments.Count == 0;

        public static TimetableTemplate Capture(Pawn p)
        {
            if (p?.timetable == null)
                return null;
            var t = new TimetableTemplate { assignments = new List<string>(24) };
            for (int h = 0; h < 24; h++)
                t.assignments.Add(p.timetable.GetAssignment(h)?.defName);
            return t;
        }

        public void ApplyTo(Pawn p)
        {
            if (p?.timetable == null || assignments == null)
                return;
            int count = assignments.Count < 24 ? assignments.Count : 24;
            for (int h = 0; h < count; h++)
            {
                var name = assignments[h];
                if (name == null)
                    continue;
                var ta = DefDatabase<TimeAssignmentDef>.GetNamedSilentFail(name);
                if (ta != null)
                    p.timetable.SetAssignment(h, ta);
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref assignments, "assignments", LookMode.Value);
        }
    }

    /// <summary>
    /// Colony-portable snapshot of a pawn's work priorities, stored as WorkTypeDef.defName -> priority.
    /// WorkTypeDefs are global defs and the values are plain ints, so the whole thing is portable.
    /// Applying tolerates work types that are disabled for the target pawn (traits/backstory/genes/
    /// age/mutant) and work types added/removed by mods (unknown defNames are simply skipped).
    /// </summary>
    public class WorkPrioritiesTemplate : IExposable
    {
        public Dictionary<string, int> priorities;

        public WorkPrioritiesTemplate() { }

        public bool IsEmpty => priorities == null || priorities.Count == 0;

        public static WorkPrioritiesTemplate Capture(Pawn p)
        {
            if (!CanWork(p))
                return null;
            var t = new WorkPrioritiesTemplate { priorities = new Dictionary<string, int>() };
            foreach (var w in WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder)
            {
                if (w == null)
                    continue;
                t.priorities[w.defName] = p.workSettings.GetPriority(w);
            }
            return t;
        }

        public void ApplyTo(Pawn p)
        {
            if (p?.workSettings == null || priorities == null || !CanWork(p))
                return;
            foreach (var kv in priorities)
            {
                var w = DefDatabase<WorkTypeDef>.GetNamedSilentFail(kv.Key);
                if (w == null || p.WorkTypeIsDisabled(w))
                    continue;
                p.workSettings.SetPriority(w, kv.Value);
            }
        }

        /// <summary>True if this pawn can take at least one work type (so a work default is meaningful).</summary>
        public static bool CanWork(Pawn p)
        {
            if (p?.workSettings == null)
                return false;
            foreach (var w in WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder)
            {
                if (w != null && !p.WorkTypeIsDisabled(w))
                    return true;
            }
            return false;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref priorities, "priorities", LookMode.Value, LookMode.Value);
        }
    }

    /// <summary>
    /// Colony-portable snapshot of small per-pawn player settings: the combat hostility response,
    /// the default medical-care category, and the self-tend toggle. All are enums/booleans living
    /// on <see cref="Pawn_PlayerSettings"/>, so they are fully portable. Hostility and medical are
    /// separate user-toggleable axes, hence the two apply methods.
    /// </summary>
    public class PawnMiscTemplate : IExposable
    {
        public HostilityResponseMode hostility = HostilityResponseMode.Flee;
        public MedicalCareCategory medCare = MedicalCareCategory.HerbalOrWorse;
        public bool selfTend;

        public PawnMiscTemplate() { }

        public static PawnMiscTemplate Capture(Pawn p)
        {
            var ps = p?.playerSettings;
            if (ps == null)
                return null;
            return new PawnMiscTemplate
            {
                hostility = ps.hostilityResponse,
                medCare = ps.medCare,
                selfTend = ps.selfTend
            };
        }

        public void ApplyHostility(Pawn p)
        {
            if (p?.playerSettings != null)
                p.playerSettings.hostilityResponse = hostility;
        }

        public void ApplyMedical(Pawn p)
        {
            if (p?.playerSettings != null)
            {
                p.playerSettings.medCare = medCare;
                p.playerSettings.selfTend = selfTend;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref hostility, "hostility", HostilityResponseMode.Flee);
            Scribe_Values.Look(ref medCare, "medCare", MedicalCareCategory.HerbalOrWorse);
            Scribe_Values.Look(ref selfTend, "selfTend", false);
        }
    }
}
