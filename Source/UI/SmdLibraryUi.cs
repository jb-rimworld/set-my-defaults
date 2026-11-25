using System;
using System.Collections.Generic;
using System.Linq;
using BetterWorkbenchDefaults.Lib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The three library pages (M6/M7): "Saved defaults" (everything stored, per area, with preview, rename, delete and a clear
    /// missing-mod marker), "Profiles" (save the current setup as a named profile, apply, duplicate, rename, delete) and "Import and
    /// export" (share strings: copy, paste, preview with the missing-mod report before anything is written). The same drawing code
    /// runs inside the framework's settings window (custom pages) and in this mod's own in-game window
    /// (<see cref="Dialog_SmdLibrary"/>), so the common path never needs Mod settings. Every button calls a named method below, so
    /// the suites can drive the same code paths without clicking.
    /// </summary>
    public static class SmdLibraryUi
    {
        public const string PageLibrary = "library", PageProfiles = "profiles", PageShare = "share";

        // ------------------------------------------------------------------------------------------------ state (per session)
        static readonly SmdUi.Scroll sLib = new SmdUi.Scroll(), sProf = new SmdUi.Scroll(), sShare = new SmdUi.Scroll();
        /// <summary>Entries whose preview is open ("b:&lt;key&gt;|&lt;name&gt;", "p:&lt;name&gt;", "pol", "col", "prof:&lt;name&gt;").</summary>
        public static readonly HashSet<string> Expanded = new HashSet<string>();
        public static string NewProfileName = "";
        public static ProfileParts NewProfileParts = ProfileParts.All;
        public static bool NewProfileFromColony = true;
        public static string ImportText = "", ImportAsName = "";
        public static string LastExportNote;
        /// <summary>Area to scroll to when the library page opens from a gizmo ("Bills", "Storage", "Plants", "Zones", "Pawns"; null = top).</summary>
        public static string JumpTo;
        static readonly Dictionary<string, float> areaY = new Dictionary<string, float>();

        // decoded import (recomputed only when the text changes)
        static string decodedFor;
        static SmdProfile decoded;
        static string decodeError;
        static MissingReport decodedMissing;
        static List<string> decodedLines;

        // family notes per library revision
        static int famRev = -1;
        static readonly Dictionary<string, string> famNote = new Dictionary<string, string>();

        // ================================================================================================ page 1: saved defaults
        public static void DrawLibrary(Rect area) => SmdUi.Probe(PageLibrary, () => SmdUi.Scrolled(area, sLib, LibraryBody));

        static float LibraryBody(float w)
        {
            float y = 0f;
            y += SmdUi.Para(0f, y, w, "WBD_LibHelp".Translate(), SmdUi.Grey) + 8f;
            y += ReadOnlyBanner(y, w);
            var c = SmdLibraryStore.Content;
            if (c.IsEmpty)
            {
                y += SmdUi.Para(0f, y, w, "WBD_LibEmpty".Translate(), Color.white) + 12f;
            }
            areaY.Clear();
            foreach (LibArea a in Enum.GetValues(typeof(LibArea)))
            {
                var sets = c.buildings.Where(b => SmdPreview.AreaOf(b) == a).OrderBy(b => DefaultBillsDatabase.KeyLabel(b.key)).ToList();
                if (sets.Count == 0) continue;
                areaY[a.ToString()] = y;
                y += SmdUi.Section(0f, y, w, SmdPreview.AreaLabel(a) + "  (" + sets.Sum(s => s.presets.Count) + ")");
                foreach (var set in sets)
                {
                    string key = set.key;
                    y += SmdUi.Line1(new Rect(0f, y, w, 24f), DefaultBillsDatabase.KeyLabel(key), Color.white);
                    string fam = FamilyNote(key);
                    if (fam != null) y += SmdUi.Para(14f, y, w - 14f, fam, SmdUi.DimGrey, GameFont.Tiny);
                    y += 2f;
                    foreach (var p in set.presets.ToList())
                    {
                        string name = p.name;
                        y += Entry(y, w, "b:" + key + "|" + name, name, set.DefaultPreset?.name == name,
                            () => DefaultBillsDatabase.SetDefaultPreset(key, name),
                            () => PromptRename(name, n => DefaultBillsDatabase.RenamePreset(key, name, n)),
                            () => ConfirmDelete(name, () => DefaultBillsDatabase.RemovePreset(key, name)),
                            SmdDefRefs.Missing(p), () => SmdPreview.PresetLines(key, p));
                    }
                    y += 8f;
                }
            }
            if (c.pawns != null && !c.pawns.IsEmpty)
            {
                areaY["Pawns"] = y;
                y += SmdUi.Section(0f, y, w, "WBD_LibArea_Pawns".Translate() + "  (" + c.pawns.presets.Count + ")");
                if (FreeWillCompat.Active) y += SmdUi.Para(0f, y, w, "WBD_PawnFreeWillNote".Translate(), SmdUi.DimGrey, GameFont.Tiny) + 4f;
                foreach (var p in c.pawns.presets.ToList())
                {
                    string name = p.name;
                    y += Entry(y, w, "p:" + name, name, c.pawns.DefaultPreset?.name == name,
                        () => PawnDefaultsStore.SetDefaultPreset(name),
                        () => PromptRename(name, n => PawnDefaultsStore.RenamePreset(name, n)),
                        () => ConfirmDelete(name, () => PawnDefaultsStore.RemovePreset(name)),
                        SmdDefRefs.Missing(p), () => SmdPreview.PawnLines(p));
                }
                y += 8f;
            }
            if (c.policies != null)
            {
                y += SmdUi.Section(0f, y, w, "WBD_LibArea_Policies".Translate());
                var b = c.policies;
                y += Entry(y, w, "pol", "WBD_LibPolicySet".Translate(), null, null, null,
                    () => ConfirmDelete("WBD_LibPolicySet".Translate(), () => SmdLibraryApi.ClearPart(ProfileParts.Policies)),
                    SmdDefRefs.Missing(b), () => SmdPreview.PolicyLines(b)) + 8f;
            }
            if (c.colony != null)
            {
                y += SmdUi.Section(0f, y, w, "WBD_LibArea_Colony".Translate());
                var b = c.colony;
                y += Entry(y, w, "col", "WBD_LibColonySet".Translate(), null, null, null,
                    () => ConfirmDelete("WBD_LibColonySet".Translate(), () => SmdLibraryApi.ClearPart(ProfileParts.Colony)),
                    SmdDefRefs.Missing(b), () => SmdPreview.ColonyLines(b)) + 8f;
            }

            // the file itself
            y += 6f;
            y += SmdUi.Section(0f, y, w, "WBD_LibFileSection".Translate());
            var s = SmdLibraryApi.Status();
            y += SmdUi.Para(0f, y, w, "WBD_LibFileLine".Translate(s.profiles), SmdUi.Grey, GameFont.Tiny) + 6f;
            if (s.lastMigration != null) y += SmdUi.Para(0f, y, w, "WBD_LibMigrated".Translate(), SmdUi.Grey, GameFont.Tiny) + 6f;
            y += SmdUi.Buttons(0f, y, w, new[]
            {
                new SmdUi.Btn("WBD_LibOpenFolder".Translate(), OpenFolder),
                new SmdUi.Btn("WBD_LibReimport".Translate(), () => Messages.Message(SmdLibraryApi.ReimportLegacy(false), MessageTypeDefOf.NeutralEvent, false),
                    !SmdLibraryStore.ReadOnly, "WBD_LibReimportTip".Translate()),
            });

            if (JumpTo != null && Event.current.type == EventType.Repaint)
            {
                if (areaY.TryGetValue(JumpTo, out float jy)) sLib.pos.y = Mathf.Max(0f, jy - 4f);
                JumpTo = null;
            }
            return y;
        }

        /// <summary>
        /// One stored entry: default radio (when the area has defaults), name, missing marker, and on the right Preview / Rename /
        /// Delete. The preview opens below. Buttons move under the name when the row is narrow. Returns the height.
        /// </summary>
        static float Entry(float y0, float w, string id, string name, bool? isDefault, Action setDefault, Action rename, Action delete,
            MissingReport missing, Func<List<string>> preview)
        {
            float y = y0;
            bool open = Expanded.Contains(id);
            var btns = new List<SmdUi.Btn>
            {
                new SmdUi.Btn(open ? "WBD_HidePreview".Translate() : "WBD_Preview".Translate(), () => Toggle(id)),
            };
            if (rename != null) btns.Add(new SmdUi.Btn("WBD_Rename".Translate(), rename, !SmdLibraryStore.ReadOnly));
            if (delete != null) btns.Add(new SmdUi.Btn("WBD_Delete".Translate(), delete, !SmdLibraryStore.ReadOnly));
            float bw = btns.Sum(b => SmdUi.BtnW(b.label, 80f)) + SmdUi.Gap * (btns.Count - 1);
            float x0 = isDefault.HasValue ? 30f : 8f;
            bool stacked = w - x0 - bw - 12f < 160f;
            float nameW = stacked ? w - x0 : w - x0 - bw - 12f;
            var row = new Rect(0f, y, w, SmdUi.RowH);
            Widgets.DrawHighlightIfMouseover(row);
            if (isDefault.HasValue)
            {
                if (Widgets.RadioButton(4f, y + 2f, isDefault.Value) && !isDefault.Value && !SmdLibraryStore.ReadOnly) setDefault?.Invoke();
                TooltipHandler.TipRegion(new Rect(0f, y, 28f, SmdUi.RowH), "WBD_SetDefault".Translate());
            }
            string shown = isDefault == true ? "WBD_PresetIsDefault".Translate(name).ToString() : name;
            SmdUi.Line1(new Rect(x0, y + 4f, nameW, 24f), shown, Color.white);
            if (!stacked) SmdUi.Buttons(w - bw, y, bw + 1f, btns, 80f);
            y += SmdUi.RowH + 2f;
            if (stacked) y += SmdUi.Buttons(x0, y, w - x0, btns, 80f) + 4f;
            string marker = SmdPreview.MissingMarker(missing);
            if (marker != null) y += SmdUi.Marker(x0, y, w - x0, marker, SmdUi.Warn, SmdPreview.MissingTip(missing)) + 4f;
            if (open)
            {
                var lines = preview?.Invoke() ?? new List<string>();
                var r = new Rect(x0, y, w - x0, 0f);
                float h = 6f;
                foreach (var l in lines) h += SmdUi.TextH(l, w - x0 - 16f, GameFont.Tiny);
                r.height = h + 4f;
                Widgets.DrawBoxSolid(r, SmdUi.CardBg);
                float ly = y + 4f;
                foreach (var l in lines) ly += SmdUi.Para(x0 + 8f, ly, w - x0 - 16f, l, SmdUi.Grey, GameFont.Tiny);
                y += r.height + 4f;
            }
            return y - y0;
        }

        static string FamilyNote(string key)
        {
            if (famRev != SmdLibraryStore.Revision) { famNote.Clear(); famRev = SmdLibraryStore.Revision; }
            if (famNote.TryGetValue(key, out var n)) return n;
            n = null;
            try
            {
                if (!SmdContent.IsZoneKey(key) && BenchFamilies.Enabled)
                {
                    var others = BenchFamilies.Members(key).Where(d => d.defName != key && !DefaultBillsDatabase.HasPresets(d.defName))
                        .Select(d => d.LabelCap.ToString()).Distinct().OrderBy(x => x).ToList();
                    if (others.Count > 0)
                        n = "WBD_LibAlsoUsedBy".Translate(string.Join(", ", others.Take(6)) + (others.Count > 6 ? " …" : ""));
                }
            }
            catch (Exception e) { WBD_Log.Warn("Bench family note failed for " + key + ": " + e.Message); }
            famNote[key] = n;
            return n;
        }

        // ================================================================================================ page 2: profiles
        public static void DrawProfiles(Rect area) => SmdUi.Probe(PageProfiles, () => SmdUi.Scrolled(area, sProf, ProfilesBody));

        static float ProfilesBody(float w)
        {
            float y = 0f;
            y += SmdUi.Para(0f, y, w, "WBD_ProfHelp".Translate(), SmdUi.Grey) + 8f;
            y += ReadOnlyBanner(y, w);

            // ---- save the current setup
            y += SmdUi.Section(0f, y, w, "WBD_ProfSaveSection".Translate());
            var c = SmdLibraryStore.Content;
            string nameL = "WBD_ProfName".Translate();
            float lw = SmdUi.TextW(nameL, GameFont.Small) + 10f;
            Widgets.Label(new Rect(0f, y + 4f, lw, 24f), nameL);
            NewProfileName = Widgets.TextField(new Rect(lw, y, Mathf.Min(w - lw, 420f), 30f), NewProfileName ?? "", 60);
            y += 36f;
            y += SmdUi.Para(0f, y, w, "WBD_ProfInclude".Translate(), Color.white) + 4f;
            var present = c.PartsPresent();
            bool inGame = Current.Game != null && Current.ProgramState == ProgramState.Playing;
            var parts = new List<(ProfileParts part, string label)>
            {
                (ProfileParts.Buildings, "WBD_PartBuildings".Translate(c.buildings.Where(b => !SmdContent.IsZoneKey(b.key)).Sum(b => b.presets.Count))),
                (ProfileParts.Zones, "WBD_PartZones".Translate(c.buildings.Where(b => SmdContent.IsZoneKey(b.key)).Sum(b => b.presets.Count))),
                (ProfileParts.Pawns, "WBD_PartPawns".Translate(c.pawns?.presets.Count ?? 0)),
                (ProfileParts.Policies, "WBD_PartPolicies".Translate()),
                (ProfileParts.Colony, "WBD_PartColony".Translate()),
            };
            int cols = w >= 760f ? 3 : w >= 460f ? 2 : 1;
            float cw = w / cols, rowMax = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                var (part, label) = parts[i];
                bool available = (present & part) != 0 || (inGame && NewProfileFromColony && (part == ProfileParts.Policies || part == ProfileParts.Colony));
                bool on = (NewProfileParts & part) != 0 && available;
                float cx = (i % cols) * cw;
                if (i % cols == 0 && i > 0) { y += rowMax + 4f; rowMax = 0f; }
                bool was = on;
                float h = SmdUi.Check(cx, y, cw - 12f, label, ref on, !available, available ? null : "WBD_PartNothing".Translate());
                rowMax = Mathf.Max(rowMax, h);
                if (on != was) NewProfileParts = on ? NewProfileParts | part : NewProfileParts & ~part;
            }
            y += rowMax + 8f;
            if (inGame)
                y += SmdUi.Check(0f, y, w, "WBD_ProfFromColony".Translate(), ref NewProfileFromColony, false, "WBD_ProfFromColonyTip".Translate()) + 8f;
            y += SmdUi.Buttons(0f, y, w, new[]
            {
                new SmdUi.Btn("WBD_ProfSave".Translate(), () => SaveNewProfile(false), !SmdLibraryStore.ReadOnly && !string.IsNullOrWhiteSpace(NewProfileName), null, true),
            }) + 14f;

            // ---- the list
            var list = SmdLibraryApi.ListProfiles().OrderBy(p => p.name).ToList();
            y += SmdUi.Section(0f, y, w, "WBD_ProfListSection".Translate(list.Count));
            if (list.Count == 0)
            {
                y += SmdUi.Para(0f, y, w, "WBD_ProfNone".Translate(), SmdUi.Grey) + 8f;
                return y;
            }
            foreach (var info in list)
                y += ProfileCard(y, w, info) + 8f;
            return y;
        }

        static float ProfileCard(float y0, float w, ProfileInfo info)
        {
            float pad = 10f, iw = w - 2f * pad, y = y0 + pad;
            string id = "prof:" + info.name;
            bool open = Expanded.Contains(id);
            // measure first (the background goes under everything)
            var lines = open ? ProfileLines(info.name) : null;
            string one = SmdPreview.OneLine(SmdLibraryStore.Library.Profile(info.name)?.content);
            string marker = SmdPreview.MissingMarker(info.missing);
            var btns = ProfileButtons(info.name, open);
            float h = pad + 26f + SmdUi.TextH(one, iw, GameFont.Tiny) + 4f
                + (string.IsNullOrEmpty(info.description) ? 0f : SmdUi.TextH(info.description, iw, GameFont.Tiny) + 4f)
                + (marker != null ? SmdUi.TextH(marker, iw - 12f, GameFont.Tiny) + 8f : 0f)
                + MeasureButtons(iw, btns) + 6f
                + (lines != null ? lines.Sum(l => SmdUi.TextH(l, iw - 16f, GameFont.Tiny)) + 12f : 0f) + pad;
            var box = new Rect(0f, y0, w, h);
            Widgets.DrawBoxSolid(box, SmdUi.CardBg);
            GUI.color = SmdUi.Line; Widgets.DrawBox(box); GUI.color = Color.white;

            string date = info.createdAt ?? "";
            float dw = SmdUi.TextW(date, GameFont.Tiny) + 4f;
            SmdUi.Line1(new Rect(pad, y, iw - dw - 8f, 26f), info.name, SmdUi.Accent, GameFont.Small);
            if (date.Length > 0) SmdUi.Line1(new Rect(w - pad - dw, y + 4f, dw, 20f), date, SmdUi.DimGrey, GameFont.Tiny);
            y += 26f;
            y += SmdUi.Para(pad, y, iw, one, SmdUi.Grey, GameFont.Tiny) + 4f;
            if (!string.IsNullOrEmpty(info.description)) y += SmdUi.Para(pad, y, iw, info.description, SmdUi.DimGrey, GameFont.Tiny) + 4f;
            if (marker != null) y += SmdUi.Marker(pad, y, iw, marker, SmdUi.Warn, SmdPreview.MissingTip(info.missing)) + 4f;
            y += SmdUi.Buttons(pad, y + 2f, iw, btns, 80f) + 6f;
            if (lines != null)
            {
                float ly = y + 4f;
                Widgets.DrawBoxSolid(new Rect(pad, y, iw, lines.Sum(l => SmdUi.TextH(l, iw - 16f, GameFont.Tiny)) + 8f), new Color(0f, 0f, 0f, 0.18f));
                foreach (var l in lines) ly += SmdUi.Para(pad + 8f, ly, iw - 16f, l, SmdUi.Grey, GameFont.Tiny);
            }
            return h;
        }

        static float MeasureButtons(float w, IList<SmdUi.Btn> btns)
        {
            float cx = 0f, rows = 1f;
            foreach (var b in btns)
            {
                float bw = Mathf.Min(SmdUi.BtnW(b.label, 80f), w);
                if (cx > 0f && cx + bw > w) { rows++; cx = 0f; }
                cx += bw + SmdUi.Gap;
            }
            return rows * SmdUi.BtnH + (rows - 1f) * SmdUi.Gap;
        }

        static List<SmdUi.Btn> ProfileButtons(string name, bool open)
        {
            bool rw = !SmdLibraryStore.ReadOnly;
            return new List<SmdUi.Btn>
            {
                new SmdUi.Btn("WBD_ProfApply".Translate(), () => ConfirmApply(name), rw, "WBD_ProfApplyTip".Translate(), true),
                new SmdUi.Btn(open ? "WBD_HidePreview".Translate() : "WBD_Preview".Translate(), () => Toggle("prof:" + name)),
                new SmdUi.Btn("WBD_ProfCopy".Translate(), () => CopyProfile(name), true, "WBD_ProfCopyTip".Translate()),
                new SmdUi.Btn("WBD_ProfDuplicate".Translate(), () => DuplicateProfile(name), rw),
                new SmdUi.Btn("WBD_Rename".Translate(), () => PromptRename(name, n => SmdLibraryApi.RenameProfile(name, n)), rw),
                new SmdUi.Btn("WBD_Delete".Translate(), () => ConfirmDelete(name, () => SmdLibraryApi.DeleteProfile(name)), rw),
            };
        }

        /// <summary>The open preview of a profile: what it holds, what applying it would replace, what is missing here.</summary>
        public static List<string> ProfileLines(string name)
        {
            var p = SmdLibraryStore.Library.Profile(name);
            if (p == null) return new List<string>();
            var l = SmdPreview.ContentLines(p.content);
            var pv = SmdLibraryApi.Preview(name);
            if (pv != null && pv.wouldReplace.Count > 0)
                l.Add("WBD_ProfWouldReplace".Translate(string.Join(", ", pv.wouldReplace.Take(10)) + (pv.wouldReplace.Count > 10 ? " …" : "")));
            return l;
        }

        // ---- actions (also called by the suites)

        /// <summary>Saves the current library (and, in game, this colony's policies and colony settings) as a profile.</summary>
        public static bool SaveNewProfile(bool overwrite)
        {
            string name = NewProfileName?.Trim();
            if (string.IsNullOrEmpty(name)) return false;
            if (!overwrite && SmdLibraryStore.Library.Profile(name) != null)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("WBD_ProfReplaceConfirm".Translate(name), () => SaveNewProfile(true), destructive: true));
                return false;
            }
            bool ok = SmdLibraryApi.SaveProfile(name, NewProfileParts, null, NewProfileFromColony, overwrite);
            if (ok)
            {
                Messages.Message("WBD_ProfSaved".Translate(name), MessageTypeDefOf.TaskCompletion, false);
                NewProfileName = "";
            }
            else
                Messages.Message(SmdLibraryStore.ReadOnly ? "WBD_LibraryReadOnly".Translate() : "WBD_ProfSaveNothing".Translate(), MessageTypeDefOf.RejectInput, false);
            return ok;
        }

        static void ConfirmApply(string name)
        {
            var p = SmdLibraryStore.Library.Profile(name);
            if (p == null) return;
            var text = "WBD_ProfApplyConfirm".Translate(name, SmdPreview.OneLine(p.content)).ToString();
            var pv = SmdLibraryApi.Preview(name);
            if (pv != null && pv.wouldReplace.Count > 0)
                text += "\n\n" + "WBD_ProfWouldReplace".Translate(string.Join(", ", pv.wouldReplace.Take(12)) + (pv.wouldReplace.Count > 12 ? " …" : ""));
            if (Current.Game != null && (p.content.colony != null || p.content.policies != null))
                text += "\n\n" + "WBD_ProfApplyInGame".Translate();
            string marker = SmdPreview.MissingMarker(pv?.missing);
            if (marker != null) text += "\n\n" + marker + ". " + "WBD_MissingTipKept".Translate();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () => ApplyProfileNow(name)));
        }

        /// <summary>Applies a profile: its presets join the library and become the defaults; in game its colony settings and policy
        /// set are applied to this colony too. Shows the result as a message.</summary>
        public static ApplyResult ApplyProfileNow(string name)
        {
            var res = SmdLibraryApi.ApplyProfile(name, ProfileParts.All, Current.Game != null, true);
            Messages.Message(res.message ?? "", res.ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, false);
            return res;
        }

        public static string DuplicateProfile(string name)
        {
            var made = SmdLibraryApi.DuplicateProfile(name);
            if (made != null) Messages.Message("WBD_ProfDuplicated".Translate(name, made), MessageTypeDefOf.TaskCompletion, false);
            else Messages.Message(DefaultBillsController.SaveFailedText(), MessageTypeDefOf.RejectInput, false);
            return made;
        }

        public static string CopyProfile(string name)
        {
            var s = SmdLibraryApi.ExportProfile(name);
            if (s == null) return null;
            if (SmdUi.Copy(s))
            {
                LastExportNote = "WBD_ShareCopied".Translate(name, s.Length.ToString("N0"));
                Messages.Message(LastExportNote, MessageTypeDefOf.TaskCompletion, false);
            }
            return s;
        }

        // ================================================================================================ page 3: import and export
        public static void DrawShare(Rect area) => SmdUi.Probe(PageShare, () => SmdUi.Scrolled(area, sShare, ShareBody));

        static float ShareBody(float w)
        {
            float y = 0f;
            y += SmdUi.Para(0f, y, w, "WBD_ShareHelp".Translate(), SmdUi.Grey) + 8f;
            y += ReadOnlyBanner(y, w);

            // ---- export
            y += SmdUi.Section(0f, y, w, "WBD_ShareExportSection".Translate());
            var profiles = SmdLibraryStore.Library.profiles.Select(p => p.name).OrderBy(n => n).ToList();
            y += SmdUi.Buttons(0f, y, w, new[]
            {
                new SmdUi.Btn("WBD_ShareCopyLibrary".Translate(), () => CopyLibrary(), !SmdLibraryStore.Content.IsEmpty, "WBD_ShareCopyLibraryTip".Translate(), true),
                new SmdUi.Btn("WBD_ShareCopyProfile".Translate(), () =>
                    Find.WindowStack.Add(new FloatMenu(profiles.Select(n => new FloatMenuOption(n, () => CopyProfile(n))).ToList())), profiles.Count > 0),
            }) + 6f;
            if (!string.IsNullOrEmpty(LastExportNote)) y += SmdUi.Para(0f, y, w, LastExportNote, SmdUi.Accent, GameFont.Tiny) + 4f;
            y += SmdUi.Para(0f, y, w, "WBD_ShareExportNote".Translate(), SmdUi.DimGrey, GameFont.Tiny) + 14f;

            // ---- import
            y += SmdUi.Section(0f, y, w, "WBD_ShareImportSection".Translate());
            y += SmdUi.Buttons(0f, y, w, new[]
            {
                new SmdUi.Btn("WBD_SharePaste".Translate(), () => SetImportText(SmdUi.Paste()), true, null, true),
                new SmdUi.Btn("WBD_ShareClear".Translate(), () => SetImportText(""), !string.IsNullOrEmpty(ImportText)),
            }) + 6f;
            y += SmdUi.Para(0f, y, w, "WBD_SharePasteHint".Translate(), SmdUi.DimGrey, GameFont.Tiny) + 2f;
            string t = Widgets.TextField(new Rect(0f, y, w, 30f), ImportText ?? "");
            if (t != ImportText) SetImportText(t);
            y += 38f;
            Decode();
            if (string.IsNullOrWhiteSpace(ImportText)) return y;
            if (decodeError != null)
            {
                y += SmdUi.Para(0f, y, w, decodeError, SmdUi.Bad) + 6f;
                return y;
            }
            // preview: nothing is written until "Import as profile"
            float pad = 10f, iw = w - 2f * pad;
            float boxY = y;
            float h = pad + 26f + decodedLines.Sum(l => SmdUi.TextH(l, iw, GameFont.Tiny)) + 8f;
            string marker = SmdPreview.MissingMarker(decodedMissing);
            if (marker != null) h += SmdUi.TextH(marker, iw - 12f, GameFont.Tiny) + 10f + SmdUi.TextH(SmdPreview.MissingTip(decodedMissing), iw, GameFont.Tiny) + 8f;
            h += pad;
            var box = new Rect(0f, boxY, w, h);
            Widgets.DrawBoxSolid(box, SmdUi.CardBg);
            GUI.color = SmdUi.Line; Widgets.DrawBox(box); GUI.color = Color.white;
            y += pad;
            SmdUi.Line1(new Rect(pad, y, iw, 26f), "WBD_ShareHolds".Translate(decoded.name, decoded.createdAt ?? "?"), SmdUi.Accent);
            y += 26f;
            if (marker != null)
            {
                // the missing-mod report comes first: it is what the player has to know before importing
                y += SmdUi.Marker(pad, y, iw, marker, SmdUi.Warn, null) + 6f;
                y += SmdUi.Para(pad, y, iw, SmdPreview.MissingTip(decodedMissing), SmdUi.Warn, GameFont.Tiny) + 8f;
            }
            foreach (var l in decodedLines) y += SmdUi.Para(pad, y, iw, l, SmdUi.Grey, GameFont.Tiny);
            y = boxY + h + 10f;
            string asL = "WBD_ShareStoreAs".Translate();
            float lw = SmdUi.TextW(asL, GameFont.Small) + 10f;
            Widgets.Label(new Rect(0f, y + 4f, lw, 24f), asL);
            ImportAsName = Widgets.TextField(new Rect(lw, y, Mathf.Min(w - lw, 420f), 30f), ImportAsName ?? "", 60);
            y += 36f;
            y += SmdUi.Buttons(0f, y, w, new[]
            {
                new SmdUi.Btn("WBD_ShareImport".Translate(), () => ImportNow(), !SmdLibraryStore.ReadOnly, null, true),
            }) + 4f;
            y += SmdUi.Para(0f, y, w, "WBD_ShareImportNote".Translate(), SmdUi.DimGrey, GameFont.Tiny) + 4f;
            return y;
        }

        public static void SetImportText(string text)
        {
            ImportText = (text ?? "").Trim();
            Decode();
            ImportAsName = decoded != null ? SmdLibraryStore.Library.UniqueProfileName(decoded.name) : "";
        }

        /// <summary>Decodes the import text once per change (no file is touched).</summary>
        static void Decode()
        {
            if (decodedFor == ImportText) return;
            decodedFor = ImportText; decoded = null; decodeError = null; decodedMissing = null; decodedLines = null;
            if (string.IsNullOrWhiteSpace(ImportText)) return;
            try
            {
                decoded = ShareCodec.Decode(ImportText);
                if (decoded.content.IsEmpty) { decodeError = "WBD_ShareEmpty".Translate(); decoded = null; return; }
                decodedMissing = SmdLibraryApi.MissingFor(decoded.content);
                decodedLines = new List<string> { SmdPreview.OneLine(decoded.content) };
                decodedLines.AddRange(SmdPreview.ContentLines(decoded.content));
            }
            catch (Exception e)
            {
                decoded = null;
                decodeError = "WBD_ShareInvalid".Translate(e is FormatException ? e.Message : e.GetType().Name);
            }
        }

        /// <summary>The decoded import, its missing-mod report and preview lines (tests read what the page shows).</summary>
        public static (SmdProfile profile, string error, MissingReport missing, List<string> lines) ImportPreview()
        {
            Decode();
            return (decoded, decodeError, decodedMissing, decodedLines);
        }

        /// <summary>Stores the decoded string as a new profile (never applied). Returns the profile name or null.</summary>
        public static string ImportNow()
        {
            var r = SmdLibraryApi.Import(ImportText, string.IsNullOrWhiteSpace(ImportAsName) ? null : ImportAsName.Trim());
            if (r.ok)
            {
                Messages.Message("WBD_ShareImported".Translate(r.profileName), MessageTypeDefOf.TaskCompletion, false);
                Expanded.Add("prof:" + r.profileName);
                SetImportText("");
                return r.profileName;
            }
            Messages.Message(r.error ?? "?", MessageTypeDefOf.RejectInput, false);
            return null;
        }

        public static string CopyLibrary()
        {
            var s = SmdLibraryApi.ExportLibrary(ProfileParts.All, null);
            if (SmdUi.Copy(s))
            {
                LastExportNote = "WBD_ShareCopied".Translate("WBD_ExportedLibraryName".Translate(), s.Length.ToString("N0"));
                Messages.Message(LastExportNote, MessageTypeDefOf.TaskCompletion, false);
            }
            return s;
        }

        // ================================================================================================ shared bits
        /// <summary>Read-only library: what happened, what the session uses, and the way out (restore the backup, open the folder).</summary>
        static float ReadOnlyBanner(float y, float w)
        {
            if (!SmdLibraryStore.ReadOnly) return 0f;
            string text = "WBD_LibraryReadOnly".Translate().ToString() + (SmdLibraryStore.UsingBackup ? " " + "WBD_LibraryUsingBackup".Translate().ToString() : "")
                + "\n" + "WBD_LibraryReadOnlyWhy".Translate(SmdLibraryStore.LoadErrorText ?? "?").ToString();
            var btns = new List<SmdUi.Btn>
            {
                new SmdUi.Btn("WBD_LibRestoreBackup".Translate(), () => RestoreBackupNow(), System.IO.File.Exists(SmdPaths.LibraryFile + LibraryFile.BackupSuffix), "WBD_LibRestoreBackupTip".Translate(), true),
                new SmdUi.Btn("WBD_LibOpenFolder".Translate(), OpenFolder),
            };
            float h = SmdUi.TextH(text, w - 16f, GameFont.Small) + 10f + MeasureButtons(w - 16f, btns) + 8f;
            Widgets.DrawBoxSolid(new Rect(0f, y, w, h), new Color(SmdUi.Bad.r, SmdUi.Bad.g, SmdUi.Bad.b, 0.15f));
            float ty = y + 5f + SmdUi.Para(8f, y + 5f, w - 16f, text, SmdUi.Bad) + 4f;
            SmdUi.Buttons(8f, ty, w - 16f, btns, 80f);
            return h + 8f;
        }

        /// <summary>"Restore the backup": Library.xml.bak becomes Library.xml again (the current file is kept as Library.broken-….xml).</summary>
        public static string RestoreBackupNow()
        {
            string err = SmdLibraryStore.RestoreBackup();
            if (err == null) Messages.Message("WBD_LibRestoreDone".Translate(), MessageTypeDefOf.TaskCompletion, false);
            else Messages.Message("WBD_LibRestoreFailed".Translate(err), MessageTypeDefOf.RejectInput, false);
            return err;
        }

        static void Toggle(string id) { if (!Expanded.Remove(id)) Expanded.Add(id); }

        static void PromptRename(string oldName, Func<string, bool> rename)
        {
            Find.WindowStack.Add(new WBD_Dialog_NamePreset("WBD_RenameTitle".Translate(oldName), oldName, newName =>
            {
                if (!rename(newName))
                    Messages.Message("WBD_RenameFailed".Translate(newName), MessageTypeDefOf.RejectInput, false);
            }));
        }

        static void ConfirmDelete(string name, Action delete)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("WBD_DeleteConfirm".Translate(name), delete, destructive: true));
        }

        static void OpenFolder()
        {
            try { System.IO.Directory.CreateDirectory(SmdPaths.LibraryFolder); Application.OpenURL("file://" + SmdPaths.LibraryFolder); }
            catch (Exception e) { Messages.Message(e.Message, MessageTypeDefOf.RejectInput, false); }
        }
    }
}
