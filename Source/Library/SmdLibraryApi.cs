using System;
using System.Collections.Generic;
using System.Linq;
using BetterWorkbenchDefaults.Lib;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>Summary of one profile for lists.</summary>
    public sealed class ProfileInfo
    {
        public string name;
        public string description;
        public string createdAt;
        public ProfileParts parts;
        public int buildingSets;
        public int buildingPresets;
        public int zonePresets;
        public int pawnPresets;
        public bool hasPolicies;
        public bool hasColony;
        public MissingReport missing = new MissingReport();
    }

    /// <summary>What applying a profile (or an import) would do, as readable lines.</summary>
    public sealed class ProfilePreview
    {
        public ProfileInfo info;
        public List<string> lines = new List<string>();
        /// <summary>"key: preset" pairs that already exist in the library and would be replaced.</summary>
        public List<string> wouldReplace = new List<string>();
        public MissingReport missing = new MissingReport();
    }

    public sealed class ApplyResult
    {
        public bool ok;
        public int merged;
        public bool colonyApplied;
        public bool policiesApplied;
        /// <summary>Name of the profile that keeps the library's previous versions of what the apply replaced (null = nothing replaced).</summary>
        public string backupProfile;
        public string message;
        public MissingReport missing = new MissingReport();
    }

    public sealed class ImportResult
    {
        public bool ok;
        public string profileName;
        public string error;
        public ProfilePreview preview;
    }

    /// <summary>State of the library for an overview / diagnosis page.</summary>
    public sealed class LibraryStatus
    {
        public string folder;
        public bool readOnly;
        public string loadError;
        public string lastMigration;
        public int buildingSets;
        public int buildingPresets;
        public int zonePresets;
        public int pawnPresets;
        public bool hasPolicies;
        public bool hasColony;
        public int profiles;
    }

    /// <summary>
    /// The data side of M7 (cross-save library with profiles and share strings). Plain functions, no UI: the kit's
    /// window (next wave) calls these. Every function is safe to call from the main menu (no game) and in game;
    /// functions that need a game say so in their result.
    /// </summary>
    public static class SmdLibraryApi
    {
        // ---------------- status ----------------

        public static LibraryStatus Status()
        {
            var c = SmdLibraryStore.Content;
            return new LibraryStatus
            {
                folder = SmdPaths.LibraryFolder,
                readOnly = SmdLibraryStore.ReadOnly,
                loadError = SmdLibraryStore.LoadError,
                lastMigration = SmdLibraryStore.LastMigration?.ToString(),
                buildingSets = c.buildings.Count(b => !SmdContent.IsZoneKey(b.key)),
                buildingPresets = c.buildings.Where(b => !SmdContent.IsZoneKey(b.key)).Sum(b => b.presets.Count),
                zonePresets = c.buildings.Where(b => SmdContent.IsZoneKey(b.key)).Sum(b => b.presets.Count),
                pawnPresets = c.pawns?.presets.Count ?? 0,
                hasPolicies = c.policies != null,
                hasColony = c.colony != null,
                profiles = SmdLibraryStore.Library.profiles.Count
            };
        }

        /// <summary>Everything in the live library that needs a mod or def that is missing now.</summary>
        public static MissingReport MissingInLibrary() => MissingFor(SmdLibraryStore.Content);

        public static MissingReport MissingFor(SmdContent c) => c == null ? new MissingReport() : SmdDefRefs.CachedFor(c, () => MissingForUncached(c));

        static MissingReport MissingForUncached(SmdContent c)
        {
            var r = new MissingReport();
            foreach (var set in c.buildings)
                foreach (var p in set.presets)
                    r.Merge(SmdDefRefs.Missing(p));
            if (c.pawns != null)
                foreach (var p in c.pawns.presets)
                    r.Merge(SmdDefRefs.Missing(p));
            r.Merge(SmdDefRefs.Missing(c.policies));
            r.Merge(SmdDefRefs.Missing(c.colony));
            return r;
        }

        // ---------------- profiles ----------------

        static int listRev = -1; static List<ProfileInfo> listCache;

        /// <summary>Summaries of every profile (cached per library revision: the Profiles page asks every frame).</summary>
        public static List<ProfileInfo> ListProfiles()
        {
            var lib = SmdLibraryStore.Library;
            if (listCache == null || listRev != SmdLibraryStore.Revision)
            {
                listCache = lib.profiles.Select(Info).ToList();
                listRev = SmdLibraryStore.Revision;
            }
            return listCache.ToList();
        }

        public static ProfileInfo Info(SmdProfile p)
        {
            var c = p.content;
            return new ProfileInfo
            {
                name = p.name,
                description = p.description,
                createdAt = p.createdAt,
                parts = c.PartsPresent(),
                buildingSets = c.buildings.Count(b => !SmdContent.IsZoneKey(b.key)),
                buildingPresets = c.buildings.Where(b => !SmdContent.IsZoneKey(b.key)).Sum(b => b.presets.Count),
                zonePresets = c.buildings.Where(b => SmdContent.IsZoneKey(b.key)).Sum(b => b.presets.Count),
                pawnPresets = c.pawns?.presets.Count ?? 0,
                hasPolicies = c.policies != null,
                hasColony = c.colony != null,
                missing = MissingFor(c)
            };
        }

        /// <summary>
        /// Save a profile: a copy of the chosen parts of the library. With <paramref name="captureLiveGame"/> and a
        /// running game, policies and colony settings are taken from THIS game instead of the library sections.
        /// Refuses an existing name unless <paramref name="overwrite"/>.
        /// </summary>
        public static bool SaveProfile(string name, ProfileParts parts, string description = null,
            bool captureLiveGame = true, bool overwrite = false)
        {
            name = name?.Trim();
            if (name.NullOrEmpty() || parts == ProfileParts.None)
                return false;
            var content = SmdLibraryStore.Content.Subset(parts);
            if (captureLiveGame && Current.Game != null)
            {
                if ((parts & ProfileParts.Policies) != 0)
                    content.policies = PolicyDefaultsStore.CaptureCurrentColonyBlock() ?? content.policies;
                if ((parts & ProfileParts.Colony) != 0)
                    content.colony = ColonySettingsStore.CaptureCurrentGameBlock() ?? content.colony;
            }
            content.RemoveEmpty();
            if (content.IsEmpty)
                return false;
            var profile = new SmdProfile
            {
                name = name,
                description = description,
                createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                content = content
            };
            return SmdLibraryStore.Mutate(lib =>
            {
                var existing = lib.Profile(name);
                if (existing != null)
                {
                    if (!overwrite)
                        return false;
                    lib.profiles.Remove(existing);
                }
                lib.profiles.Add(profile);
                return true;
            });
        }

        /// <summary>Copy of a profile under a free name ("Tribal start (2)"). Returns the new name, or null.</summary>
        public static string DuplicateProfile(string name)
        {
            string made = null;
            SmdLibraryStore.Mutate(lib =>
            {
                var p = lib.Profile(name);
                if (p == null)
                    return false;
                var copy = p.Clone();
                copy.name = lib.UniqueProfileName(name);
                copy.createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd");
                lib.profiles.Add(copy);
                made = copy.name;
                return true;
            });
            return SmdLibraryStore.Library.Profile(made) != null ? made : null;
        }

        /// <summary>Removes one area's content from the live library (the "Delete" of a policy set or colony settings entry).</summary>
        public static bool ClearPart(ProfileParts part) =>
            SmdLibraryStore.Mutate(lib =>
            {
                bool changed = false;
                if ((part & ProfileParts.Policies) != 0 && lib.content.policies != null) { lib.content.policies = null; changed = true; }
                if ((part & ProfileParts.Colony) != 0 && lib.content.colony != null) { lib.content.colony = null; changed = true; }
                if ((part & ProfileParts.Pawns) != 0 && lib.content.pawns != null) { lib.content.pawns = null; changed = true; }
                return changed;
            });

        public static bool DeleteProfile(string name) =>
            SmdLibraryStore.Mutate(lib => lib.profiles.RemoveAll(p => p.name == name) > 0);

        public static bool RenameProfile(string oldName, string newName)
        {
            newName = newName?.Trim();
            if (newName.NullOrEmpty())
                return false;
            return SmdLibraryStore.Mutate(lib =>
            {
                var p = lib.Profile(oldName);
                if (p == null || lib.Profile(newName) != null)
                    return false;
                p.name = newName;
                return true;
            });
        }

        /// <summary>What <see cref="ApplyProfile"/> would do, without doing it.</summary>
        public static ProfilePreview Preview(string name, ProfileParts parts = ProfileParts.All)
        {
            var p = SmdLibraryStore.Library.Profile(name);
            return p == null ? null : PreviewOf(p, parts);
        }

        public static ProfilePreview PreviewOf(SmdProfile p, ProfileParts parts)
        {
            var sub = p.content.Subset(parts);
            var pv = new ProfilePreview { info = Info(new SmdProfile { name = p.name, description = p.description, createdAt = p.createdAt, content = sub }) };
            var lib = SmdLibraryStore.Content;
            foreach (var set in sub.buildings)
                pv.lines.Add("WBD_PreviewPresetsFor".Translate(set.presets.Count, DefaultBillsDatabase.KeyLabel(set.key),
                    string.Join(", ", set.presets.Select(x => x.name))).ToString());
            if (sub.pawns != null)
                pv.lines.Add("WBD_PreviewPawnPresets".Translate(sub.pawns.presets.Count, string.Join(", ", sub.pawns.presets.Select(x => x.name))).ToString());
            if (sub.policies != null)
                pv.lines.Add("WBD_PreviewPolicies".Translate().ToString());
            if (sub.colony != null)
                pv.lines.Add("WBD_PreviewColony".Translate().ToString());
            // only entries that exist here AND differ count as replaced (an identical copy changes nothing)
            pv.wouldReplace = ReplacedLabels(lib.ReplacedBy(sub, parts));
            pv.missing = pv.info.missing;
            return pv;
        }

        /// <summary>"Electric stove: Kitchen", "Colonists: Worker", "Policy set", "Colony settings".</summary>
        public static List<string> ReplacedLabels(SmdContent replaced)
        {
            var l = new List<string>();
            if (replaced == null) return l;
            foreach (var set in replaced.buildings)
                foreach (var x in set.presets)
                    l.Add(DefaultBillsDatabase.KeyLabel(set.key) + ": " + x.name);
            if (replaced.pawns != null)
                foreach (var x in replaced.pawns.presets)
                    l.Add("WBD_PreviewColonists".Translate() + ": " + x.name);
            if (replaced.policies != null) l.Add("WBD_PreviewPolicies".Translate().ToString());
            if (replaced.colony != null) l.Add("WBD_PreviewColony".Translate().ToString());
            return l;
        }

        /// <summary>
        /// Apply a profile: its presets go into the library (same names are replaced, existing defaults are kept),
        /// and with <paramref name="applyToCurrentGame"/> its colony settings and policy set are applied to the running
        /// game right away (new games get them anyway through the new-game settings).
        /// </summary>
        public static ApplyResult ApplyProfile(string name, ProfileParts parts, bool applyToCurrentGame) =>
            ApplyProfile(name, parts, applyToCurrentGame, false);

        /// <summary>
        /// Like <see cref="ApplyProfile(string, ProfileParts, bool)"/>; with <paramref name="takeDefaults"/> the profile's defaults also
        /// become the library's defaults (what the profile UI and the first-start card do: from now on new buildings, zones and
        /// colonists get the profile's presets). Presets of the library that the profile does not name are kept.
        /// </summary>
        public static ApplyResult ApplyProfile(string name, ProfileParts parts, bool applyToCurrentGame, bool takeDefaults)
        {
            var res = new ApplyResult();
            var p = SmdLibraryStore.Library.Profile(name);
            if (p == null)
            {
                res.message = "WBD_ProfileNotFound".Translate(name ?? "?");
                return res;
            }
            int merged = 0;
            string backup = null;
            var incoming = p.content.Clone();
            bool saved = SmdLibraryStore.Mutate(lib =>
            {
                // Nothing the profile overwrites is lost: the library's previous versions of every entry that really changes go
                // into a profile of their own ("Before Tribal start"), in the same write.
                var replaced = lib.content.ReplacedBy(incoming, parts);
                merged = lib.content.MergeFrom(incoming, parts, takeDefaults);
                backup = null;
                if (!replaced.IsEmpty)
                {
                    backup = lib.UniqueProfileName("WBD_BackupProfileName".Translate(p.name).ToString());
                    lib.profiles.Add(new SmdProfile
                    {
                        name = backup,
                        createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        description = "WBD_BackupProfileDesc".Translate(p.name, DateTime.Now.ToString("yyyy-MM-dd HH:mm")).ToString(),
                        content = replaced
                    });
                }
                return merged > 0;
            });
            res.merged = merged;
            res.missing = MissingFor(p.content.Subset(parts));
            // not saved: either nothing to merge (fine) or the write failed (the edit may not even have run: lock, read-only)
            if (!saved && (merged > 0 || SmdLibraryStore.LastSaveError != null || SmdLibraryStore.ReadOnly))
            {
                res.message = DefaultBillsController.SaveFailedText();
                return res;
            }
            res.backupProfile = saved ? backup : null;
            if (applyToCurrentGame && Current.Game != null)
            {
                if ((parts & ProfileParts.Colony) != 0 && p.content.colony != null && SmdSections.On(SmdSections.Colony))
                    res.colonyApplied = ColonySettingsStore.ApplyToCurrentGame();
                if ((parts & ProfileParts.Policies) != 0 && p.content.policies != null && SmdSections.On(SmdSections.Policies))
                    res.policiesApplied = PolicyDefaultsStore.ApplyToCurrentColony();
            }
            res.ok = true;
            res.message = "WBD_ProfileApplied".Translate(p.name, merged).ToString();
            if (res.backupProfile != null)
                res.message += " " + "WBD_BackupProfileMade".Translate(res.backupProfile);
            if (!res.missing.IsEmpty)
                res.message += " " + res.missing.Summary();
            return res;
        }

        // ---------------- share strings ----------------

        /// <summary>Share string of one profile (null when it does not exist).</summary>
        public static string ExportProfile(string name)
        {
            var p = SmdLibraryStore.Library.Profile(name);
            return p == null ? null : ShareCodec.Encode(p);
        }

        /// <summary>Share string of the chosen parts of the live library (as a profile named <paramref name="name"/>).</summary>
        public static string ExportLibrary(ProfileParts parts = ProfileParts.All, string name = null)
        {
            var content = SmdLibraryStore.Content.Subset(parts);
            content.RemoveEmpty();
            return ShareCodec.Encode(new SmdProfile
            {
                name = name.NullOrEmpty() ? "WBD_ExportedLibraryName".Translate().ToString() : name,
                createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                content = content
            });
        }

        /// <summary>Decode a share string and show what it holds, without storing anything.</summary>
        public static ImportResult PreviewShare(string share)
        {
            var r = new ImportResult();
            try
            {
                var p = ShareCodec.Decode(share);
                r.profileName = p.name;
                r.preview = PreviewOf(p, ProfileParts.All);
                r.ok = true;
            }
            catch (FormatException e)
            {
                r.error = "WBD_ShareInvalid".Translate(e.Message).ToString();
            }
            return r;
        }

        /// <summary>
        /// Import a share string as a new PROFILE (never applied automatically). A taken name gets " (2)".
        /// The result carries the preview including what the profile needs that is missing here.
        /// </summary>
        public static ImportResult Import(string share, string asName = null)
        {
            var r = PreviewShare(share);
            if (!r.ok)
                return r;
            SmdProfile p;
            try
            {
                p = ShareCodec.Decode(share);
            }
            catch (FormatException e)
            {
                r.ok = false;
                r.error = "WBD_ShareInvalid".Translate(e.Message).ToString();
                return r;
            }
            if (p.content.IsEmpty)
            {
                r.ok = false;
                r.error = "WBD_ShareEmpty".Translate().ToString();
                return r;
            }
            string finalName = null;
            bool saved = SmdLibraryStore.Mutate(lib =>
            {
                finalName = lib.UniqueProfileName(asName.NullOrEmpty() ? p.name : asName);
                p.name = finalName;
                lib.profiles.Add(p);
                return true;
            });
            r.ok = saved;
            r.profileName = finalName;
            if (!saved)
                r.error = DefaultBillsController.SaveFailedText();
            return r;
        }

        // ---------------- legacy ----------------

        /// <summary>Import the 1.x preset files again (players who kept saving with the 1.6 version). Returns a readable line.</summary>
        public static string ReimportLegacy(bool overwrite)
        {
            var r = SmdLibraryStore.ReimportLegacy(overwrite);
            return r == null ? DefaultBillsController.SaveFailedText() : r.ToString();
        }
    }
}
