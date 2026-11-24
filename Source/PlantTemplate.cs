using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The plant setting of a growing zone or a plant grower building (hydroponics basin, plant pot, modded growers)
    /// (M10). Stored as the plant's defName; zones also keep their sow/cut toggles.
    /// A plant that cannot grow on the target (wrong sow tag, research missing, mod off) is skipped and the target
    /// keeps the vanilla choice.
    /// </summary>
    public class PlantTemplate : IExposable
    {
        public string plant;
        public bool allowSow = true;
        public bool allowCut = true;
        public bool hasZoneToggles;

        public bool IsEmpty => plant.NullOrEmpty() && !hasZoneToggles;

        public static PlantTemplate Capture(IPlantToGrowSettable settable)
        {
            if (settable == null)
                return null;
            var t = new PlantTemplate();
            try
            {
                t.plant = settable.GetPlantDefToGrow()?.defName;
            }
            catch
            {
                t.plant = null;
            }
            if (settable is Zone_Growing zone)
            {
                t.allowSow = zone.allowSow;
                t.allowCut = zone.allowCut;
                t.hasZoneToggles = true;
            }
            return t.IsEmpty ? null : t;
        }

        /// <summary>The stored plant if it can be sown on <paramref name="settable"/> right now, else null.</summary>
        public ThingDef ResolvePlantFor(IPlantToGrowSettable settable, bool checkCells)
        {
            if (plant.NullOrEmpty() || settable == null)
                return null;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(plant);
            if (def?.plant == null)
                return null;
            bool fits;
            if (checkCells)
                fits = PlantUtility.CanSowOnGrower(def, settable);
            else if (settable is Zone)
                fits = def.plant.sowTags != null && def.plant.sowTags.Contains("Ground");
            else
                fits = PlantUtility.CanSowOnGrower(def, settable);
            if (!fits)
                return null;
            var map = settable.Map;
            if (map != null && !Command_SetPlantToGrow.IsPlantAvailable(def, map))
                return null;
            return def;
        }

        /// <summary>Apply; returns true when the plant was set. <paramref name="checkCells"/> is false for a zone that has no cells yet.</summary>
        public bool ApplyTo(IPlantToGrowSettable settable, bool checkCells = true)
        {
            if (settable == null)
                return false;
            bool set = false;
            var def = ResolvePlantFor(settable, checkCells);
            if (def != null)
            {
                settable.SetPlantDefToGrow(def);
                set = true;
            }
            if (hasZoneToggles && settable is Zone_Growing zone)
            {
                zone.allowSow = allowSow;
                zone.allowCut = allowCut;
            }
            return set;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref plant, "plant");
            Scribe_Values.Look(ref allowSow, "allowSow", true);
            Scribe_Values.Look(ref allowCut, "allowCut", true);
            Scribe_Values.Look(ref hasZoneToggles, "hasZoneToggles", false);
        }
    }
}
