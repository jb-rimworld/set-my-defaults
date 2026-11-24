namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The switchable feature areas of Set my Defaults. <see cref="On"/> is the ONE function every patch, gizmo and
    /// auto-apply path asks before doing anything. Since 2.0 it asks JB Framework (<c>Kit.On(id)</c>).
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

        /// <summary>The area is on right now (available, not paused by the framework's error fence, switched on in this save).</summary>
        public static bool On(string id) => WBD_Mod.K?.On(id) == true;
    }
}
