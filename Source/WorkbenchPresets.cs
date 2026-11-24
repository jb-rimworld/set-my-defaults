using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>One named, saved configuration (bills + storage + splitter) for a building type.</summary>
    public class WorkbenchPreset : IExposable
    {
        public string name;
        public ConfigSnapshot config;

        public WorkbenchPreset() { }

        public WorkbenchPreset(string name, ConfigSnapshot config)
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
    /// All saved presets for one building type. Exactly one is the "default" (auto-applied to a
    /// freshly built workbench); the rest are extra named setups the player can load on demand
    /// (e.g. a normal butcher setup plus a "Cannibal" setup).
    /// </summary>
    public class WorkbenchPresets : IExposable
    {
        public const string DefaultName = "Default";

        public List<WorkbenchPreset> presets = new List<WorkbenchPreset>();
        public string defaultName;

        public bool IsEmpty => presets == null || presets.Count == 0;

        public WorkbenchPreset DefaultPreset
        {
            get
            {
                if (IsEmpty)
                    return null;
                return presets.FirstOrDefault(p => p.name == defaultName) ?? presets[0];
            }
        }

        public ConfigSnapshot DefaultConfig => DefaultPreset?.config;

        public WorkbenchPreset Get(string name)
        {
            return presets?.FirstOrDefault(p => p.name == name);
        }

        public bool IsDefault(string name)
        {
            var def = DefaultPreset;
            return def != null && def.name == name;
        }

        /// <summary>Add a new preset or overwrite an existing one with the same name.</summary>
        public void AddOrReplace(string name, ConfigSnapshot config, bool makeDefault)
        {
            var existing = Get(name);
            if (existing != null)
                existing.config = config;
            else
                presets.Add(new WorkbenchPreset(name, config));

            // First preset is always the default; otherwise honour the request, and make sure
            // defaultName always points at a real preset.
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
                return false; // empty or name already taken
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

        /// <summary>Ensure exactly-one-valid-default: clear when empty, else point at a real preset.</summary>
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

        /// <summary>Shallow copy (new list + preset wrappers, shared immutable configs) for safe edit-then-commit.</summary>
        public WorkbenchPresets Clone()
        {
            return new WorkbenchPresets
            {
                defaultName = defaultName,
                presets = (presets ?? new List<WorkbenchPreset>())
                    .Where(p => p != null)
                    .Select(p => new WorkbenchPreset(p.name, p.config))
                    .ToList()
            };
        }

        /// <summary>Build a one-preset set from a legacy (pre-presets) single config.</summary>
        public static WorkbenchPresets FromLegacy(ConfigSnapshot legacy)
        {
            var wp = new WorkbenchPresets();
            wp.presets.Add(new WorkbenchPreset(DefaultName, legacy));
            wp.defaultName = DefaultName;
            return wp;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref defaultName, "defaultPreset");
            Scribe_Collections.Look(ref presets, "presets", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && presets == null)
                presets = new List<WorkbenchPreset>();
        }
    }
}
