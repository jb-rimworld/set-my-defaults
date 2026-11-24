using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Optional "Blueprints Forked" support: a blueprint remembers each captured building's
    /// bills + storage settings (+ PRF splitter routing) and recreates them across save games.
    ///
    /// Three reflection-resolved hooks on Blueprints.BuildableInfo:
    ///  - ctor(Thing, IntVec3): snapshot the live building's config into a weak table;
    ///  - ExposeData(): scribe that snapshot, so it rides BOTH the colony save and the exported
    ///    cross-save XML side-file (Blueprints serialises entries with LookMode.Deep);
    ///  - Designate(IntVec3 origin): when the blueprint is stamped, queue the snapshot for the
    ///    target cell so our SpawnSetup hook applies it once the building is built.
    /// No compile-time or load-time dependency on Blueprints: absent => these no-op.
    /// </summary>
    public static class BlueprintsCompat
    {
        private static readonly ConditionalWeakTable<object, ConfigSnapshot> _captured =
            new ConditionalWeakTable<object, ConfigSnapshot>();

        private static Type _buildableInfoType;
        private static PropertyInfo _positionProp;
        private static PropertyInfo _buildableDefProp;

        /// <summary>All three Blueprints Forked hooks are in place.</summary>
        public static bool Hooked { get; private set; }

        public static void Apply(Harmony harmony)
        {
            _buildableInfoType = GenTypes.GetTypeInAnyAssembly("Blueprints.BuildableInfo");
            if (_buildableInfoType == null)
                return; // Blueprints Forked not installed

            try
            {
                _positionProp = _buildableInfoType.GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                _buildableDefProp = _buildableInfoType.GetProperty("BuildableDef", BindingFlags.Public | BindingFlags.Instance);

                var ctor = AccessTools.Constructor(_buildableInfoType, new[] { typeof(Thing), typeof(IntVec3) });
                var expose = AccessTools.Method(_buildableInfoType, "ExposeData");
                var designate = AccessTools.Method(_buildableInfoType, "Designate", new[] { typeof(IntVec3) });

                if (ctor != null)
                    harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(BlueprintsCompat), nameof(Ctor_Postfix)));
                if (expose != null)
                    harmony.Patch(expose, postfix: new HarmonyMethod(typeof(BlueprintsCompat), nameof(ExposeData_Postfix)));
                if (designate != null)
                    harmony.Patch(designate, postfix: new HarmonyMethod(typeof(BlueprintsCompat), nameof(Designate_Postfix)));

                if (ctor != null && expose != null && designate != null
                    && _positionProp != null && _buildableDefProp != null)
                    Hooked = true;
                if (Hooked)
                    WBD_Log.Msg("Blueprints Forked support enabled (blueprints remember bills & storage).");
                else
                    WBD_Log.Warn("Blueprints Forked detected but some hooks were missing; integration may be partial.");
            }
            catch (Exception e)
            {
                WBD_Log.Warn("Could not hook Blueprints Forked; blueprints will not remember settings: " + e.Message);
            }
        }

        // Capture the live building's config when a blueprint entry is created from it.
        // For non-building entries (blueprints/frames/terrain) Capture returns null -> ignored.
        public static void Ctor_Postfix(object __instance, Thing thing)
        {
            if (WBD_Mod.Settings?.compatBlueprints == false)
                return;
            try
            {
                var cfg = ConfigSnapshot.Capture(thing);
                if (cfg != null)
                {
                    _captured.Remove(__instance);
                    _captured.Add(__instance, cfg);
                }
            }
            catch (Exception e) { WBD_Log.Warn("Blueprint capture failed: " + e.Message); }
        }

        // Persist captured config inside the blueprint entry's Scribe node.
        public static void ExposeData_Postfix(object __instance)
        {
            try
            {
                ConfigSnapshot cfg = null;
                // On save AND in the post-LoadingVars passes we must hand Scribe the SAME object
                // we already loaded, so the snapshot's nested ThingFilters get their
                // ResolvingCrossRefs + PostLoadInit passes (otherwise their allowed defs, which
                // are LookMode.Def cross-refs, resolve to empty filters).
                if (Scribe.mode == LoadSaveMode.Saving
                    || Scribe.mode == LoadSaveMode.ResolvingCrossRefs
                    || Scribe.mode == LoadSaveMode.PostLoadInit)
                    _captured.TryGetValue(__instance, out cfg);

                Scribe_Deep.Look(ref cfg, "wbdConfig");

                if (Scribe.mode == LoadSaveMode.LoadingVars && cfg != null)
                {
                    _captured.Remove(__instance);
                    _captured.Add(__instance, cfg);
                }
            }
            catch (Exception e) { WBD_Log.Warn("Blueprint config scribe failed: " + e.Message); }
        }

        // On placement, queue the captured config at the target cell for SpawnSetup to consume.
        public static void Designate_Postfix(object __instance, IntVec3 origin)
        {
            try
            {
                if (WBD_Mod.Settings?.compatBlueprints == false)
                    return;
                if (!_captured.TryGetValue(__instance, out var cfg) || cfg == null)
                    return;
                var map = Find.CurrentMap;
                if (map == null || _positionProp == null || _buildableDefProp == null)
                    return;
                var offset = (IntVec3)_positionProp.GetValue(__instance, null);
                var def = _buildableDefProp.GetValue(__instance, null) as ThingDef;
                if (def == null)
                    return;
                WBD_PendingConfigs.For(map)?.Register(origin + offset, def, cfg);
            }
            catch (Exception e) { WBD_Log.Warn("Blueprint designate hook failed: " + e.Message); }
        }
    }
}
