namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The switchable feature areas of Set my Defaults. <see cref="On"/> is the ONE function every patch, gizmo and
    /// auto-apply path asks before doing anything. Today it reads plain fields of <see cref="WBD_Settings"/>; the shared
    /// kit (JB.Kit, next wave) replaces the body with <c>Kit.On(id)</c> without touching any caller.
    /// Switching an area off stops its gizmos, buttons and auto-apply; saved presets are kept.
    /// </summary>
    public static class SmdSections
    {
        public const string Buildings = "buildings";
        public const string Zones = "zones";
        public const string Pawns = "pawns";
        public const string Policies = "policies";
        public const string Colony = "colony";
        public const string Library = "library";

        public static readonly string[] All = { Buildings, Zones, Pawns, Policies, Colony, Library };

        public static bool On(string id)
        {
            var s = WBD_Mod.Settings;
            if (s == null)
                return false;
            switch (id)
            {
                case Buildings: return s.sectionBuildings;
                case Zones: return s.sectionZones;
                case Pawns: return s.sectionPawns;
                case Policies: return s.sectionPolicies;
                case Colony: return s.sectionColony;
                case Library: return s.sectionLibrary;
            }
            return false;
        }
    }
}
