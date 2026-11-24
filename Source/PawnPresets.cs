using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>One named, saved pawn configuration (schedule + work + policies + misc settings).
    /// Deliberately kept as its own type (parallel to <see cref="WorkbenchPreset"/>) so the live,
    /// shipped building serialization is never touched by the pawn feature.</summary>
    public class PawnPreset : IExposable
    {
        public string name;
        public PawnConfigSnapshot config;

        public PawnPreset() { }

        public PawnPreset(string name, PawnConfigSnapshot config)
        {
            this.name = name;
            this.config = config;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Deep.Look(ref config, "config");
        }
    }

    /// <summary>
    /// All saved pawn presets. Exactly one is the "default" (auto-applied once to each genuinely-new
    /// colonist); the rest are extra named setups the player can apply on demand via the pawn gizmo.
    /// Mirrors the semantics of <see cref="WorkbenchPresets"/> but for a single global pawn set.
    /// </summary>
    public class PawnPresets : IExposable
    {
        public const string DefaultName = "Default";

        public List<PawnPreset> presets = new List<PawnPreset>();
        public string defaultName;

        public bool IsEmpty => presets == null || presets.Count == 0;

        public PawnPreset DefaultPreset
        {
            get
            {
                if (IsEmpty)
                    return null;
                return presets.FirstOrDefault(p => p.name == defaultName) ?? presets[0];
            }
        }

        public PawnConfigSnapshot DefaultConfig => DefaultPreset?.config;

        public PawnPreset Get(string name) => presets?.FirstOrDefault(p => p.name == name);

        public bool IsDefault(string name)
        {
            var def = DefaultPreset;
            return def != null && def.name == name;
        }

        /// <summary>Add a new preset or overwrite an existing one with the same name.</summary>
        public void AddOrReplace(string name, PawnConfigSnapshot config, bool makeDefault)
        {
            var existing = Get(name);
            if (existing != null)
                existing.config = config;
            else
                presets.Add(new PawnPreset(name, config));

            if (makeDefault || defaultName == null || Get(defaultName) == null)
                defaultName = name;
        }

        public bool Remove(string name)
        {
            var p = Get(name);
            if (p == null)
                return false;
            presets.Remove(p);
            if (defaultName == name)
                defaultName = presets.Count > 0 ? presets[0].name : null;
            return true;
        }

        public bool Rename(string oldName, string newName)
        {
            newName = newName?.Trim();
            if (newName.NullOrEmpty() || Get(newName) != null)
                return false;
            var p = Get(oldName);
            if (p == null)
                return false;
            p.name = newName;
            if (defaultName == oldName)
                defaultName = newName;
            return true;
        }

        public bool SetDefault(string name)
        {
            if (Get(name) == null)
                return false;
            defaultName = name;
            return true;
        }

        public void NormalizeDefault()
        {
            if (IsEmpty)
            {
                defaultName = null;
                return;
            }
            if (defaultName == null || Get(defaultName) == null)
                defaultName = presets[0].name;
        }

        /// <summary>Shallow copy (new list + preset wrappers, shared configs) for edit-then-commit.</summary>
        public PawnPresets Clone()
        {
            return new PawnPresets
            {
                defaultName = defaultName,
                presets = (presets ?? new List<PawnPreset>())
                    .Where(p => p != null)
                    .Select(p => new PawnPreset(p.name, p.config))
                    .ToList()
            };
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref defaultName, "defaultPreset");
            Scribe_Collections.Look(ref presets, "presets", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && presets == null)
                presets = new List<PawnPreset>();
        }
    }
}
