using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

// Verse-free on purpose: everything in Source/Lib compiles into the mod AND into the standalone test project
// (Source/Tests/SmdLibTests), which proves the file format, the migration and the share codec without the game.
namespace BetterWorkbenchDefaults.Lib
{
    /// <summary>A mod a stored preset needs (recorded when the preset is saved).</summary>
    public sealed class ModRef
    {
        public string id;   // packageId as the player sees it (lower case), e.g. "vanillaexpanded.vcooke"
        public string name; // display name at save time, e.g. "Vanilla Cooking Expanded"

        public ModRef() { }
        public ModRef(string id, string name) { this.id = id; this.name = name; }

        public XElement ToXml() => new XElement("mod", new XAttribute("id", id ?? ""), new XAttribute("name", name ?? ""));

        public static ModRef FromXml(XElement e) =>
            new ModRef((string)e.Attribute("id") ?? "", (string)e.Attribute("name") ?? "");

        public static List<ModRef> ListFromXml(XElement parent)
        {
            var list = new List<ModRef>();
            var req = parent?.Element("requiredMods");
            if (req == null)
                return list;
            foreach (var m in req.Elements("mod"))
            {
                var r = FromXml(m);
                if (!string.IsNullOrEmpty(r.id) && list.All(x => x.id != r.id))
                    list.Add(r);
            }
            return list;
        }

        public static XElement ListToXml(IEnumerable<ModRef> mods)
        {
            var list = mods?.Where(m => m != null && !string.IsNullOrEmpty(m.id)).ToList();
            if (list == null || list.Count == 0)
                return null;
            return new XElement("requiredMods", list.Select(m => m.ToXml()));
        }
    }

    /// <summary>
    /// One stored, named entry. <see cref="config"/> is the RAW stored element and is the single source of truth:
    /// it is never rebuilt from live game objects after loading, so defs from a mod that is switched off right now
    /// stay in the file untouched (M4). Its children use the same element names the 1.x Scribe files used.
    /// </summary>
    public sealed class StoredPreset
    {
        public string name;
        public XElement config;               // root element named "config"
        public List<ModRef> requiredMods = new List<ModRef>();
        public string savedAt;                // ISO date (informational)

        public StoredPreset Clone() => new StoredPreset
        {
            name = name,
            config = config != null ? new XElement(config) : null,
            requiredMods = requiredMods.Select(m => new ModRef(m.id, m.name)).ToList(),
            savedAt = savedAt
        };

        public XElement ToXml()
        {
            var e = new XElement("preset", new XAttribute("name", name ?? ""));
            if (!string.IsNullOrEmpty(savedAt))
                e.Add(new XAttribute("savedAt", savedAt));
            var req = ModRef.ListToXml(requiredMods);
            if (req != null)
                e.Add(req);
            e.Add(config != null ? new XElement(config) { Name = "config" } : new XElement("config"));
            return e;
        }

        public static StoredPreset FromXml(XElement e) => new StoredPreset
        {
            name = (string)e.Attribute("name") ?? "",
            savedAt = (string)e.Attribute("savedAt"),
            requiredMods = ModRef.ListFromXml(e),
            config = e.Element("config") != null ? new XElement(e.Element("config")) : new XElement("config")
        };
    }

    /// <summary>
    /// A keyed list of named presets with exactly one default. Keys: a building defName, "zone:stockpile",
    /// "zone:dumping", "zone:growing", or "pawns". Same rules as the 1.x WorkbenchPresets/PawnPresets
    /// (first preset becomes the default, rename refuses duplicates, default always points at a real preset).
    /// </summary>
    public sealed class PresetSet
    {
        public string key;
        public string defaultName;
        public List<StoredPreset> presets = new List<StoredPreset>();

        public bool IsEmpty => presets.Count == 0;

        public StoredPreset Get(string name) => presets.FirstOrDefault(p => p.name == name);

        public StoredPreset DefaultPreset => IsEmpty ? null : (Get(defaultName) ?? presets[0]);

        public void AddOrReplace(StoredPreset preset, bool makeDefault)
        {
            var i = presets.FindIndex(p => p.name == preset.name);
            if (i >= 0)
                presets[i] = preset;
            else
                presets.Add(preset);
            if (makeDefault || defaultName == null || Get(defaultName) == null)
                defaultName = preset.name;
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
            if (string.IsNullOrEmpty(newName) || Get(newName) != null)
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

        public void Normalize()
        {
            presets.RemoveAll(p => p == null || string.IsNullOrEmpty(p.name));
            // duplicate names: keep the first (a hand-edited file must not break the set)
            var seen = new HashSet<string>();
            presets.RemoveAll(p => !seen.Add(p.name));
            if (IsEmpty)
                defaultName = null;
            else if (defaultName == null || Get(defaultName) == null)
                defaultName = presets[0].name;
        }

        public PresetSet Clone() => new PresetSet
        {
            key = key,
            defaultName = defaultName,
            presets = presets.Select(p => p.Clone()).ToList()
        };

        public XElement ToXml(string elementName = "set")
        {
            var e = new XElement(elementName);
            if (key != null && elementName == "set")
                e.Add(new XAttribute("key", key));
            if (defaultName != null)
                e.Add(new XAttribute("default", defaultName));
            foreach (var p in presets)
                e.Add(p.ToXml());
            return e;
        }

        public static PresetSet FromXml(XElement e, string keyOverride = null)
        {
            var set = new PresetSet
            {
                key = keyOverride ?? (string)e.Attribute("key"),
                defaultName = (string)e.Attribute("default")
            };
            foreach (var p in e.Elements("preset"))
                set.presets.Add(StoredPreset.FromXml(p));
            set.Normalize();
            return set;
        }
    }

    /// <summary>A single stored block (policy set, colony settings) with the mods it needs.</summary>
    public sealed class StoredBlock
    {
        public XElement config;
        public List<ModRef> requiredMods = new List<ModRef>();
        public string savedAt;

        public StoredBlock Clone() => new StoredBlock
        {
            config = config != null ? new XElement(config) : null,
            requiredMods = requiredMods.Select(m => new ModRef(m.id, m.name)).ToList(),
            savedAt = savedAt
        };

        public XElement ToXml(string elementName)
        {
            var e = new XElement(elementName);
            if (!string.IsNullOrEmpty(savedAt))
                e.Add(new XAttribute("savedAt", savedAt));
            var req = ModRef.ListToXml(requiredMods);
            if (req != null)
                e.Add(req);
            e.Add(config != null ? new XElement(config) { Name = "config" } : new XElement("config"));
            return e;
        }

        public static StoredBlock FromXml(XElement e)
        {
            if (e == null)
                return null;
            return new StoredBlock
            {
                savedAt = (string)e.Attribute("savedAt"),
                requiredMods = ModRef.ListFromXml(e),
                config = e.Element("config") != null ? new XElement(e.Element("config")) : new XElement("config")
            };
        }
    }

    /// <summary>Which parts of a library a profile carries / an apply touches.</summary>
    [Flags]
    public enum ProfileParts
    {
        None = 0,
        Buildings = 1,   // building presets (bills, storage, plants) incl. bench families
        Zones = 2,       // stockpile / dumping / growing zone presets
        Pawns = 4,       // colonist presets
        Policies = 8,    // the cross-save policy set
        Colony = 16,     // colony settings (medical defaults, auto home, auto rebuild, manual priorities)
        All = Buildings | Zones | Pawns | Policies | Colony
    }

    /// <summary>
    /// A named profile: a self-contained bundle of library sections ("Tribal start", "Mechanitor run"). It is a copy,
    /// not a reference, so it survives later edits of the library and can be shared as one string.
    /// </summary>
    public sealed class SmdProfile
    {
        public string name;
        public string description;
        public string createdAt;
        public SmdContent content = new SmdContent();

        public SmdProfile Clone() => new SmdProfile
        {
            name = name,
            description = description,
            createdAt = createdAt,
            content = content.Clone()
        };

        public XElement ToXml()
        {
            var e = new XElement("profile", new XAttribute("name", name ?? ""));
            if (!string.IsNullOrEmpty(createdAt))
                e.Add(new XAttribute("createdAt", createdAt));
            if (!string.IsNullOrEmpty(description))
                e.Add(new XElement("description", description));
            content.WriteInto(e);
            return e;
        }

        public static SmdProfile FromXml(XElement e) => new SmdProfile
        {
            name = (string)e.Attribute("name") ?? "",
            createdAt = (string)e.Attribute("createdAt"),
            description = (string)e.Element("description"),
            content = SmdContent.ReadFrom(e)
        };
    }

    /// <summary>The data sections shared by the library itself and by every profile.</summary>
    public sealed class SmdContent
    {
        public List<PresetSet> buildings = new List<PresetSet>(); // building defName and zone:* keys
        public PresetSet pawns;                                    // key "pawns"
        public StoredBlock policies;                               // PolicyDefaultsSet (Scribe layout)
        public StoredBlock colony;                                 // ColonySettingsTemplate (Scribe layout)

        public const string ZonePrefix = "zone:";

        public static bool IsZoneKey(string key) => key != null && key.StartsWith(ZonePrefix, StringComparison.Ordinal);

        public PresetSet Buildings(string key) => buildings.FirstOrDefault(s => s.key == key);

        public PresetSet GetOrAddBuildings(string key)
        {
            var s = Buildings(key);
            if (s == null)
            {
                s = new PresetSet { key = key };
                buildings.Add(s);
            }
            return s;
        }

        public void RemoveEmpty()
        {
            foreach (var s in buildings)
                s.Normalize();
            buildings.RemoveAll(s => s == null || string.IsNullOrEmpty(s.key) || s.IsEmpty);
            pawns?.Normalize();
            if (pawns != null && pawns.IsEmpty)
                pawns = null;
        }

        public bool IsEmpty => buildings.Count == 0 && (pawns == null || pawns.IsEmpty) && policies == null && colony == null;

        public SmdContent Clone() => new SmdContent
        {
            buildings = buildings.Select(b => b.Clone()).ToList(),
            pawns = pawns?.Clone(),
            policies = policies?.Clone(),
            colony = colony?.Clone()
        };

        /// <summary>Only the requested parts (a deep copy).</summary>
        public SmdContent Subset(ProfileParts parts)
        {
            var c = new SmdContent();
            foreach (var b in buildings)
            {
                bool zone = IsZoneKey(b.key);
                if ((zone && (parts & ProfileParts.Zones) != 0) || (!zone && (parts & ProfileParts.Buildings) != 0))
                    c.buildings.Add(b.Clone());
            }
            if ((parts & ProfileParts.Pawns) != 0)
                c.pawns = pawns?.Clone();
            if ((parts & ProfileParts.Policies) != 0)
                c.policies = policies?.Clone();
            if ((parts & ProfileParts.Colony) != 0)
                c.colony = colony?.Clone();
            return c;
        }

        public ProfileParts PartsPresent()
        {
            var p = ProfileParts.None;
            if (buildings.Any(b => !IsZoneKey(b.key))) p |= ProfileParts.Buildings;
            if (buildings.Any(b => IsZoneKey(b.key))) p |= ProfileParts.Zones;
            if (pawns != null && !pawns.IsEmpty) p |= ProfileParts.Pawns;
            if (policies != null) p |= ProfileParts.Policies;
            if (colony != null) p |= ProfileParts.Colony;
            return p;
        }

        /// <summary>
        /// Merge <paramref name="other"/> into this content: presets with the same key and name are replaced, new ones
        /// are added, existing defaults are kept unless the key had no presets before (then the incoming default wins).
        /// Policy set and colony block are replaced when the incoming content carries them. Returns the count merged.
        /// </summary>
        public int MergeFrom(SmdContent other, ProfileParts parts) => MergeFrom(other, parts, false);

        /// <summary>
        /// Like <see cref="MergeFrom(SmdContent, ProfileParts)"/>; with <paramref name="takeDefaults"/> the incoming defaults also become
        /// the defaults here (applying a profile: its presets are what new buildings and colonists get from now on). Other presets stay.
        /// </summary>
        public int MergeFrom(SmdContent other, ProfileParts parts, bool takeDefaults)
        {
            int n = 0;
            foreach (var inc in other.Subset(parts).buildings)
            {
                var mine = Buildings(inc.key);
                if (mine == null)
                {
                    buildings.Add(inc.Clone());
                    n += inc.presets.Count;
                    continue;
                }
                foreach (var p in inc.presets)
                {
                    mine.AddOrReplace(p.Clone(), makeDefault: false);
                    n++;
                }
                if (takeDefaults && inc.DefaultPreset != null)
                    mine.SetDefault(inc.DefaultPreset.name);
            }
            if ((parts & ProfileParts.Pawns) != 0 && other.pawns != null && !other.pawns.IsEmpty)
            {
                if (pawns == null || pawns.IsEmpty)
                {
                    pawns = other.pawns.Clone();
                    pawns.key = "pawns";
                }
                else
                {
                    foreach (var p in other.pawns.presets)
                        pawns.AddOrReplace(p.Clone(), makeDefault: false);
                    if (takeDefaults && other.pawns.DefaultPreset != null)
                        pawns.SetDefault(other.pawns.DefaultPreset.name);
                }
                n += other.pawns.presets.Count;
            }
            if ((parts & ProfileParts.Policies) != 0 && other.policies != null)
            {
                policies = other.policies.Clone();
                n++;
            }
            if ((parts & ProfileParts.Colony) != 0 && other.colony != null)
            {
                colony = other.colony.Clone();
                n++;
            }
            RemoveEmpty();
            return n;
        }

        public void WriteInto(XElement parent)
        {
            if (buildings.Count > 0)
                parent.Add(new XElement("buildings", buildings.OrderBy(b => b.key, StringComparer.Ordinal).Select(b => b.ToXml("set"))));
            if (pawns != null && !pawns.IsEmpty)
                parent.Add(pawns.ToXml("pawns"));
            if (policies != null)
                parent.Add(policies.ToXml("policies"));
            if (colony != null)
                parent.Add(colony.ToXml("colony"));
        }

        public static SmdContent ReadFrom(XElement parent)
        {
            var c = new SmdContent();
            var b = parent.Element("buildings");
            if (b != null)
            {
                foreach (var s in b.Elements("set"))
                {
                    var set = PresetSet.FromXml(s);
                    if (string.IsNullOrEmpty(set.key) || set.IsEmpty)
                        continue;
                    var existing = c.Buildings(set.key);
                    if (existing == null)
                        c.buildings.Add(set);
                    else
                        foreach (var p in set.presets)
                            existing.AddOrReplace(p, false);
                }
            }
            var pw = parent.Element("pawns");
            if (pw != null)
            {
                c.pawns = PresetSet.FromXml(pw, "pawns");
                if (c.pawns.IsEmpty)
                    c.pawns = null;
            }
            c.policies = StoredBlock.FromXml(parent.Element("policies"));
            c.colony = StoredBlock.FromXml(parent.Element("colony"));
            return c;
        }
    }

    /// <summary>
    /// The cross-save library (one file, <c>SetMyDefaults/Library.xml</c>). Holds the live presets every colony uses
    /// plus named profiles. Unknown top-level elements (written by a newer version) are kept and written back.
    /// </summary>
    public sealed class SmdLibrary
    {
        public const string RootName = "SetMyDefaultsLibrary";
        public const int CurrentVersion = 4;

        public int version = CurrentVersion;
        public SmdContent content = new SmdContent();
        public List<SmdProfile> profiles = new List<SmdProfile>();
        public List<XElement> migrationLog = new List<XElement>();
        public List<XElement> unknownElements = new List<XElement>();

        private static readonly HashSet<string> Known = new HashSet<string>
            { "buildings", "pawns", "policies", "colony", "profiles", "migration" };

        public SmdProfile Profile(string name) => profiles.FirstOrDefault(p => p.name == name);

        /// <summary>A free profile name: "name", else "name (2)", "name (3)", ...</summary>
        public string UniqueProfileName(string wanted)
        {
            wanted = string.IsNullOrWhiteSpace(wanted) ? "Profile" : wanted.Trim();
            if (Profile(wanted) == null)
                return wanted;
            for (int i = 2; ; i++)
            {
                var n = wanted + " (" + i + ")";
                if (Profile(n) == null)
                    return n;
            }
        }

        public SmdLibrary Clone()
        {
            var l = new SmdLibrary
            {
                version = version,
                content = content.Clone(),
                profiles = profiles.Select(p => p.Clone()).ToList(),
                migrationLog = migrationLog.Select(e => new XElement(e)).ToList(),
                unknownElements = unknownElements.Select(e => new XElement(e)).ToList()
            };
            return l;
        }

        public XElement ToXml()
        {
            var root = new XElement(RootName, new XAttribute("version", Math.Max(version, CurrentVersion)));
            if (migrationLog.Count > 0)
                root.Add(new XElement("migration", migrationLog.Select(e => new XElement(e))));
            content.WriteInto(root);
            if (profiles.Count > 0)
                root.Add(new XElement("profiles", profiles.Select(p => p.ToXml())));
            foreach (var u in unknownElements)
                root.Add(new XElement(u));
            return root;
        }

        public static SmdLibrary FromXml(XElement root)
        {
            if (root == null || root.Name.LocalName != RootName)
                throw new FormatException("Not a Set my Defaults library (root element " + root?.Name + ").");
            var lib = new SmdLibrary();
            int v;
            lib.version = int.TryParse((string)root.Attribute("version"), out v) ? v : 0;
            lib.content = SmdContent.ReadFrom(root);
            var prof = root.Element("profiles");
            if (prof != null)
                foreach (var p in prof.Elements("profile"))
                {
                    var sp = SmdProfile.FromXml(p);
                    if (!string.IsNullOrEmpty(sp.name) && lib.Profile(sp.name) == null)
                        lib.profiles.Add(sp);
                }
            var mig = root.Element("migration");
            if (mig != null)
                lib.migrationLog = mig.Elements().Select(e => new XElement(e)).ToList();
            lib.unknownElements = root.Elements().Where(e => !Known.Contains(e.Name.LocalName)).Select(e => new XElement(e)).ToList();
            return lib;
        }
    }
}
