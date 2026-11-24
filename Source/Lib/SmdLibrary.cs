using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

// Verse-free on purpose: everything in Source/Lib compiles into the mod AND into the standalone test project
// (Source/Tests/SmdLibTests), which proves the file format, the migration and the share codec without the game.
namespace BetterWorkbenchDefaults.Lib
{
    /// <summary>
    /// What a newer version of the mod wrote into an element that this version does not know (attributes and child elements).
    /// It is kept and written back unchanged, so opening Library.xml with an older version never drops a newer version's data
    /// (one library is shared by every game on the machine, and players switch mod versions).
    /// </summary>
    public sealed class XExtra
    {
        public List<XAttribute> attrs = new List<XAttribute>();
        public List<XElement> elems = new List<XElement>();

        public bool IsEmpty => attrs.Count == 0 && elems.Count == 0;

        public static XExtra Capture(XElement e, string[] knownAttrs, string[] knownElems)
        {
            var x = new XExtra();
            if (e == null)
                return x;
            foreach (var a in e.Attributes())
                if (!a.IsNamespaceDeclaration && Array.IndexOf(knownAttrs, a.Name.LocalName) < 0)
                    x.attrs.Add(new XAttribute(a));
            foreach (var c in e.Elements())
                if (Array.IndexOf(knownElems, c.Name.LocalName) < 0)
                    x.elems.Add(new XElement(c));
            return x;
        }

        public void WriteTo(XElement e)
        {
            foreach (var a in attrs)
                if (e.Attribute(a.Name) == null)
                    e.Add(new XAttribute(a));
            foreach (var c in elems)
                e.Add(new XElement(c));
        }

        public XExtra Clone() => new XExtra
        {
            attrs = attrs.Select(a => new XAttribute(a)).ToList(),
            elems = elems.Select(c => new XElement(c)).ToList()
        };
    }

    /// <summary>A mod a stored preset needs (recorded when the preset is saved).</summary>
    public sealed class ModRef
    {
        public string id;   // packageId as the player sees it (lower case), e.g. "vanillaexpanded.vcooke"
        public string name; // display name at save time, e.g. "Vanilla Cooking Expanded"
        public XExtra extra = new XExtra();

        public ModRef() { }
        public ModRef(string id, string name) { this.id = id; this.name = name; }

        public ModRef Clone() => new ModRef(id, name) { extra = extra.Clone() };

        public XElement ToXml()
        {
            var e = new XElement("mod", new XAttribute("id", id ?? ""), new XAttribute("name", name ?? ""));
            extra.WriteTo(e);
            return e;
        }

        public static ModRef FromXml(XElement e) =>
            new ModRef((string)e.Attribute("id") ?? "", (string)e.Attribute("name") ?? "")
            {
                extra = XExtra.Capture(e, new[] { "id", "name" }, new string[0])
            };

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
        public XExtra extra = new XExtra();   // what a newer version wrote here (kept)

        private static readonly string[] KnownAttrs = { "name", "savedAt" };
        private static readonly string[] KnownElems = { "requiredMods", "config" };

        public StoredPreset Clone() => new StoredPreset
        {
            name = name,
            config = config != null ? new XElement(config) : null,
            requiredMods = requiredMods.Select(m => m.Clone()).ToList(),
            savedAt = savedAt,
            extra = extra.Clone()
        };

        /// <summary>Same stored content (config, required mods and kept extras; the name and the date do not count).</summary>
        public bool SameContent(StoredPreset other)
        {
            if (other == null)
                return false;
            var a = ToXml(); var b = other.ToXml();
            a.SetAttributeValue("name", null); a.SetAttributeValue("savedAt", null);
            b.SetAttributeValue("name", null); b.SetAttributeValue("savedAt", null);
            return XNode.DeepEquals(a, b);
        }

        public XElement ToXml()
        {
            var e = new XElement("preset", new XAttribute("name", name ?? ""));
            if (!string.IsNullOrEmpty(savedAt))
                e.Add(new XAttribute("savedAt", savedAt));
            var req = ModRef.ListToXml(requiredMods);
            if (req != null)
                e.Add(req);
            e.Add(config != null ? new XElement(config) { Name = "config" } : new XElement("config"));
            extra.WriteTo(e);
            return e;
        }

        public static StoredPreset FromXml(XElement e) => new StoredPreset
        {
            name = (string)e.Attribute("name") ?? "",
            savedAt = (string)e.Attribute("savedAt"),
            requiredMods = ModRef.ListFromXml(e),
            config = e.Element("config") != null ? new XElement(e.Element("config")) : new XElement("config"),
            extra = XExtra.Capture(e, KnownAttrs, KnownElems)
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
        public XExtra extra = new XExtra();   // what a newer version wrote here (kept)

        public bool IsEmpty => presets.Count == 0;

        /// <summary>A name that no preset of this set uses: "name", else "name (2)", "name (3)", ...</summary>
        public string UniqueName(string wanted)
        {
            wanted = string.IsNullOrWhiteSpace(wanted) ? "Preset" : wanted.Trim();
            if (Get(wanted) == null)
                return wanted;
            for (int i = 2; ; i++)
            {
                var n = wanted + " (" + i + ")";
                if (Get(n) == null)
                    return n;
            }
        }

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
            presets.RemoveAll(p => p == null);
            // Empty or duplicate names (a hand-edited file, two sets of one key): nothing is dropped, the later ones get a free
            // name ("Stove (2)"); the first keeps its name, so a default that names it still points at the same preset.
            var seen = new HashSet<string>();
            foreach (var p in presets)
            {
                if (string.IsNullOrWhiteSpace(p.name))
                    p.name = "Preset";
                if (seen.Add(p.name))
                    continue;
                int i = 2;
                while (seen.Contains(p.name + " (" + i + ")") || presets.Any(o => o != p && o.name == p.name + " (" + i + ")"))
                    i++;
                p.name = p.name + " (" + i + ")";
                seen.Add(p.name);
            }
            if (IsEmpty)
                defaultName = null;
            else if (defaultName == null || Get(defaultName) == null)
                defaultName = presets[0].name;
        }

        public PresetSet Clone() => new PresetSet
        {
            key = key,
            defaultName = defaultName,
            presets = presets.Select(p => p.Clone()).ToList(),
            extra = extra.Clone()
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
            extra.WriteTo(e);
            return e;
        }

        public static PresetSet FromXml(XElement e, string keyOverride = null)
        {
            var set = new PresetSet
            {
                key = keyOverride ?? (string)e.Attribute("key"),
                defaultName = (string)e.Attribute("default"),
                extra = XExtra.Capture(e, new[] { "key", "default" }, new[] { "preset" })
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
        public XExtra extra = new XExtra();   // what a newer version wrote here (kept)

        public StoredBlock Clone() => new StoredBlock
        {
            config = config != null ? new XElement(config) : null,
            requiredMods = requiredMods.Select(m => m.Clone()).ToList(),
            savedAt = savedAt,
            extra = extra.Clone()
        };

        /// <summary>Same stored content (the date does not count).</summary>
        public bool SameContent(StoredBlock other)
        {
            if (other == null)
                return false;
            var a = ToXml("b"); var b = other.ToXml("b");
            a.SetAttributeValue("savedAt", null); b.SetAttributeValue("savedAt", null);
            return XNode.DeepEquals(a, b);
        }

        public XElement ToXml(string elementName)
        {
            var e = new XElement(elementName);
            if (!string.IsNullOrEmpty(savedAt))
                e.Add(new XAttribute("savedAt", savedAt));
            var req = ModRef.ListToXml(requiredMods);
            if (req != null)
                e.Add(req);
            e.Add(config != null ? new XElement(config) { Name = "config" } : new XElement("config"));
            extra.WriteTo(e);
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
                config = e.Element("config") != null ? new XElement(e.Element("config")) : new XElement("config"),
                extra = XExtra.Capture(e, new[] { "savedAt" }, new[] { "requiredMods", "config" })
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
        public XExtra extra = new XExtra();   // what a newer version wrote here, e.g. a section this version has no idea of (kept)

        public SmdProfile Clone() => new SmdProfile
        {
            name = name,
            description = description,
            createdAt = createdAt,
            content = content.Clone(),
            extra = extra.Clone()
        };

        public XElement ToXml()
        {
            var e = new XElement("profile", new XAttribute("name", name ?? ""));
            if (!string.IsNullOrEmpty(createdAt))
                e.Add(new XAttribute("createdAt", createdAt));
            if (!string.IsNullOrEmpty(description))
                e.Add(new XElement("description", description));
            content.WriteInto(e);
            extra.WriteTo(e);
            return e;
        }

        public static SmdProfile FromXml(XElement e) => new SmdProfile
        {
            name = (string)e.Attribute("name") ?? "",
            createdAt = (string)e.Attribute("createdAt"),
            description = (string)e.Element("description"),
            content = SmdContent.ReadFrom(e),
            extra = XExtra.Capture(e, new[] { "name", "createdAt" }, new[] { "description", "buildings", "pawns", "policies", "colony" })
        };
    }

    /// <summary>The data sections shared by the library itself and by every profile.</summary>
    public sealed class SmdContent
    {
        public List<PresetSet> buildings = new List<PresetSet>(); // building defName and zone:* keys
        public PresetSet pawns;                                    // key "pawns"
        public StoredBlock policies;                               // PolicyDefaultsSet (Scribe layout)
        public StoredBlock colony;                                 // ColonySettingsTemplate (Scribe layout)
        public XExtra buildingsExtra = new XExtra();               // what a newer version wrote into <buildings> (kept)

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
            colony = colony?.Clone(),
            buildingsExtra = buildingsExtra.Clone()
        };

        /// <summary>
        /// The entries of THIS content that merging <paramref name="incoming"/> (<paramref name="parts"/>) would overwrite with something
        /// different: same key and name but other content, or a policy set / colony block that differs. A deep copy, with this content's
        /// defaults where they are among the copied presets. Empty when the merge changes nothing that exists (identical entries do not count).
        /// </summary>
        public SmdContent ReplacedBy(SmdContent incoming, ProfileParts parts)
        {
            var r = new SmdContent();
            if (incoming == null)
                return r;
            var sub = incoming.Subset(parts);
            foreach (var inc in sub.buildings)
            {
                var mine = Buildings(inc.key);
                if (mine == null)
                    continue;
                PresetSet hit = null;
                foreach (var p in inc.presets)
                {
                    var old = mine.Get(p.name);
                    if (old == null || old.SameContent(p))
                        continue;
                    if (hit == null)
                        r.buildings.Add(hit = new PresetSet { key = mine.key });
                    hit.presets.Add(old.Clone());
                }
                if (hit != null)
                    hit.defaultName = hit.Get(mine.defaultName) != null ? mine.defaultName : hit.presets[0].name;
            }
            if (sub.pawns != null && pawns != null)
            {
                PresetSet hit = null;
                foreach (var p in sub.pawns.presets)
                {
                    var old = pawns.Get(p.name);
                    if (old == null || old.SameContent(p))
                        continue;
                    if (hit == null)
                        hit = new PresetSet { key = "pawns" };
                    hit.presets.Add(old.Clone());
                }
                if (hit != null)
                {
                    hit.defaultName = hit.Get(pawns.defaultName) != null ? pawns.defaultName : hit.presets[0].name;
                    r.pawns = hit;
                }
            }
            if (sub.policies != null && policies != null && !policies.SameContent(sub.policies))
                r.policies = policies.Clone();
            if (sub.colony != null && colony != null && !colony.SameContent(sub.colony))
                r.colony = colony.Clone();
            return r;
        }

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
            if (buildings.Count > 0 || !buildingsExtra.IsEmpty)
            {
                var be = new XElement("buildings", buildings.OrderBy(b => b.key, StringComparer.Ordinal).Select(b => b.ToXml("set")));
                buildingsExtra.WriteTo(be);
                parent.Add(be);
            }
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
                c.buildingsExtra = XExtra.Capture(b, new string[0], new[] { "set" });
                foreach (var s in b.Elements("set"))
                {
                    var set = PresetSet.FromXml(s);
                    if (string.IsNullOrEmpty(set.key) || set.IsEmpty)
                    {
                        // a set without a key cannot be used, but it is not ours to delete
                        if (!set.IsEmpty)
                            c.buildingsExtra.elems.Add(new XElement(s));
                        continue;
                    }
                    var existing = c.Buildings(set.key);
                    if (existing == null)
                        c.buildings.Add(set);
                    else
                    {
                        // two sets of one key (hand-edited file): keep every preset, same names get a free name
                        existing.presets.AddRange(set.presets);
                        existing.Normalize();
                    }
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
        public XExtra rootExtra = new XExtra();       // unknown attributes of the root (kept)
        public XExtra profilesExtra = new XExtra();   // unknown content of <profiles> (kept)
        /// <summary>The format version of the file as read (a file of a NEWER version is larger than <see cref="CurrentVersion"/>).</summary>
        public int FileVersion => version;

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
                unknownElements = unknownElements.Select(e => new XElement(e)).ToList(),
                rootExtra = rootExtra.Clone(),
                profilesExtra = profilesExtra.Clone()
            };
            return l;
        }

        public XElement ToXml()
        {
            // version: never lowered (a newer version's file stays marked as such); writtenBy: the version that wrote it last
            var root = new XElement(RootName, new XAttribute("version", Math.Max(version, CurrentVersion)), new XAttribute("writtenBy", CurrentVersion));
            rootExtra.WriteTo(root);
            if (migrationLog.Count > 0)
                root.Add(new XElement("migration", migrationLog.Select(e => new XElement(e))));
            content.WriteInto(root);
            if (profiles.Count > 0 || !profilesExtra.IsEmpty)
            {
                var pe = new XElement("profiles", profiles.Select(p => p.ToXml()));
                profilesExtra.WriteTo(pe);
                root.Add(pe);
            }
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
            lib.rootExtra = XExtra.Capture(root, new[] { "version", "writtenBy" }, Known.ToArray().Concat(root.Elements().Select(e => e.Name.LocalName)).ToArray());
            lib.content = SmdContent.ReadFrom(root);
            var prof = root.Element("profiles");
            if (prof != null)
            {
                lib.profilesExtra = XExtra.Capture(prof, new string[0], new[] { "profile" });
                foreach (var p in prof.Elements("profile"))
                {
                    // nothing is dropped: an empty or taken name (hand-edited file) gets a free one
                    var sp = SmdProfile.FromXml(p);
                    sp.name = lib.UniqueProfileName(sp.name);
                    lib.profiles.Add(sp);
                }
            }
            var mig = root.Element("migration");
            if (mig != null)
                lib.migrationLog = mig.Elements().Select(e => new XElement(e)).ToList();
            lib.unknownElements = root.Elements().Where(e => !Known.Contains(e.Name.LocalName)).Select(e => new XElement(e)).ToList();
            return lib;
        }
    }
}
