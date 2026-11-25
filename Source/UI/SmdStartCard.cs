using System;
using System.Collections.Generic;
using System.Linq;
using BetterWorkbenchDefaults.Lib;
using JBFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Set my Defaults' part of its welcome card (M7). The card itself is JB Framework's (KitRegistry.WelcomeCard: once per new colony,
    /// once when the mod joins a save, never while the player switched welcome cards off; header, pitch, the three "what happens by
    /// itself" presets, "use for all new colonies", "don't show again"). This class draws the part above the presets: on a new colony
    /// "Use my defaults" (preselected, the obvious choice) or one of the saved profiles, each with a one-line preview and a missing-mod
    /// marker, plus "also bring my saved policies"; when the mod joined an existing save one line says nothing was changed. The choice
    /// is applied exactly once, when the card closes in any way; a profile that would replace presets of the same name asks first.
    /// </summary>
    public static class SmdStartCard
    {
        /// <summary>"" = my defaults, else a profile name.</summary>
        public static string Choice = "";
        /// <summary>Starts from "Apply the saved policies to new games" (off by default: applying a set is the player's decision).</summary>
        public static bool AlsoPolicies;
        /// <summary>How often a choice was applied this session (tests: exactly once).</summary>
        public static int AppliedCount;
        /// <summary>The message the last applied profile showed (tests).</summary>
        public static string LastMessage;
        /// <summary>The confirmation the last close opened (tests), or null.</summary>
        public static Dialog_SmdReplaceConfirm LastConfirm;

        // what the chosen profile would replace, per choice and library revision (not recomputed every frame)
        static string replFor; static int replRev = -1; static List<string> repl = new List<string>();

        static bool Joined => SmdGameData.startCard == SmdGameData.StartJoined;

        /// <summary>A game started or was loaded: the card's choice starts fresh.</summary>
        internal static void Begin()
        {
            Choice = "";
            AlsoPolicies = WBD_Mod.Settings?.policyAutoApplyOnNewGame ?? false;
            replFor = null; replRev = -1; repl = new List<string>();
            LastConfirm = null;
        }

        /// <summary>Library entries the chosen profile would overwrite with a different version ("Electric stove: Kitchen").</summary>
        public static List<string> WouldReplace()
        {
            if (Joined || string.IsNullOrEmpty(Choice)) return new List<string>();
            if (replFor != Choice || replRev != SmdLibraryStore.Revision)
            {
                replFor = Choice; replRev = SmdLibraryStore.Revision;
                try { repl = SmdLibraryApi.Preview(Choice)?.wouldReplace ?? new List<string>(); }
                catch (Exception e) { repl = new List<string>(); WBD_Log.Warn("Could not preview the profile \"" + Choice + "\": " + e.Message); }
            }
            return repl;
        }

        /// <summary>
        /// KitCard.WhenClosed: applies the choice once. A profile that replaces presets of the same name asks first and lists them
        /// (their old versions are kept as a profile anyway); "No" starts the colony with the player's own defaults instead.
        /// </summary>
        internal static void Closed(bool joined)
        {
            if (SmdGameData.startCard == SmdGameData.StartDone) return;
            if (joined || Joined) { Apply("", false, true); return; }
            string choice = Choice ?? "";
            var r = WouldReplace();
            if (choice != "" && r.Count > 0 && Find.WindowStack != null)
            {
                string list = string.Join(", ", r.Take(12)) + (r.Count > 12 ? " …" : "");
                var d = new Dialog_SmdReplaceConfirm(choice, list, AlsoPolicies);
                LastConfirm = d;
                Find.WindowStack.Add(d);
                return;
            }
            Apply(choice, choice == "" && AlsoPolicies, false);
        }

        /// <summary>KitCard.WithDraw: the part above the presets; returns its height.</summary>
        internal static float Draw(Rect r)
        {
            float h = 0f;
            SmdUi.Probe("card", () => h = Body(r));
            return h;
        }

        static float Body(Rect area)
        {
            float x = area.x, y = area.y, w = area.width;
            if (SmdGameData.startCard == SmdGameData.StartDone) return 0f;
            if (Joined)
                return SmdUi.Para(x, y, w, "WBD_CardPitchJoined".Translate(), SmdUi.Grey) + 4f;
            bool profilesOn = SmdSections.On(SmdSections.Library);
            float y0 = y;
            y += SmdUi.Section(x, y, w, "WBD_CardStartWith".Translate());
            var lib = SmdLibraryStore.Content;
            string mine = lib.IsEmpty ? "WBD_CardMineEmpty".Translate().ToString() : SmdPreview.OneLine(lib);
            y += Option(x, y, w, "", "WBD_CardMine".Translate(), "WBD_CardRecommended".Translate(), mine, SmdLibraryApi.MissingInLibrary()) + 8f;
            if (profilesOn)
            {
                var profiles = SmdLibraryStore.Library.profiles.OrderBy(p => p.name).ToList();
                foreach (var p in profiles)
                    y += Option(x, y, w, p.name, p.name, null, SmdPreview.OneLine(p.content), SmdLibraryApi.MissingFor(p.content)) + 8f;
                if (profiles.Count == 0)
                    y += SmdUi.Para(x + 4f, y, w - 8f, "WBD_CardNoProfiles".Translate(), SmdUi.DimGrey, GameFont.Tiny) + 8f;
            }
            if (Choice == "" && PolicyDefaultsStore.HasSet() && !SmdGameData.policiesAppliedAtStart && SmdSections.On(SmdSections.Policies))
                y += SmdUi.Check(x + 4f, y, w - 8f, "WBD_CardAlsoPolicies".Translate(), ref AlsoPolicies, false, "WBD_CardAlsoPoliciesTip".Translate()) + 8f;
            return y - y0;
        }

        /// <summary>One choice row (radio card): title (+ chip), then the one-line preview with the missing-mod note after it.
        /// Compact on purpose, so the presets below stay in view on small screens. Returns its height.</summary>
        static float Option(float x, float y, float w, string id, string title, string chip, string line, MissingReport missing)
        {
            bool sel = Choice == id;
            float iw = w - 56f;
            string marker = SmdPreview.MissingMarker(missing);
            if (marker != null) line += "  <color=#F29E59>" + marker + "</color>";
            if (sel && id != "")
            {
                var r0 = WouldReplace();
                if (r0.Count > 0) line += "\n<color=#E8C46A>" + "WBD_CardWillReplace".Translate(r0.Count, "WBD_BackupProfileName".Translate(id)) + "</color>";
            }
            // small screens (1280x720): unselected rows show only their title (preview as tooltip), so the framework's "what happens
            // by itself" choices below stay close to view
            bool compact = UI.screenHeight < 900 && !sel;
            if (compact)
            {
                var rc = new Rect(x, y, w, 34f);
                Widgets.DrawBoxSolid(rc, SmdUi.CardBg);
                GUI.color = SmdUi.Line; Widgets.DrawBox(rc, 1); GUI.color = Color.white;
                if (Mouse.IsOver(rc)) Widgets.DrawHighlight(rc);
                Widgets.RadioButton(rc.x + 12f, rc.y + 5f, false);
                float cwc = chip != null ? SmdUi.TextW(chip, GameFont.Tiny) + 16f : 0f;
                float twc = Mathf.Min(SmdUi.TextW(title, GameFont.Small) + 4f, iw - cwc - 8f);
                SmdUi.Line1(new Rect(rc.x + 46f, rc.y + 5f, twc, 24f), title, Color.white);
                if (chip != null) SmdUi.Marker(rc.x + 46f + twc + 8f, rc.y + 8f, cwc + 12f, chip, SmdUi.Accent, null);
                if (marker != null) SmdUi.Line1(new Rect(rc.x + 46f + twc + cwc + 28f, rc.y + 7f, Mathf.Max(0f, iw - twc - cwc - 28f), 22f), marker, SmdUi.Warn, GameFont.Tiny);
                TooltipHandler.TipRegion(rc, line + (marker != null ? "\n\n" + SmdPreview.MissingTip(missing) : ""));
                if (Widgets.ButtonInvisible(rc)) Choice = id;
                return rc.height;
            }
            float h = 8f + 24f + SmdUi.TextH(line, iw, GameFont.Tiny) + 8f;
            var r = new Rect(x, y, w, h);
            Widgets.DrawBoxSolid(r, sel ? SmdUi.AccentSoft : SmdUi.CardBg);
            GUI.color = sel ? SmdUi.Accent : SmdUi.Line; Widgets.DrawBox(r, sel ? 2 : 1); GUI.color = Color.white;
            if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
            Widgets.RadioButton(r.x + 12f, r.y + (h - 24f) / 2f, sel);
            float cw = chip != null ? SmdUi.TextW(chip, GameFont.Tiny) + 16f : 0f;
            float titleW = Mathf.Min(SmdUi.TextW(title, GameFont.Small) + 4f, iw - cw - 8f);
            SmdUi.Line1(new Rect(r.x + 46f, r.y + 7f, titleW, 24f), title, sel ? SmdUi.Accent : Color.white);
            if (chip != null) SmdUi.Marker(r.x + 46f + titleW + 8f, r.y + 10f, cw + 12f, chip, SmdUi.Accent, null);
            SmdUi.Para(r.x + 46f, r.y + 32f, iw, line, SmdUi.Grey, GameFont.Tiny);
            if (marker != null) TooltipHandler.TipRegion(r, SmdPreview.MissingTip(missing));
            if (Widgets.ButtonInvisible(r)) Choice = id;
            return h;
        }

        static PawnConfigSnapshot ReadPawnDefault(SmdProfile p)
        {
            var sp = p?.content?.pawns?.DefaultPreset;
            if (sp?.config == null) return null;
            var snap = new PawnConfigSnapshot();
            return SmdScribe.Read(sp.config, snap.ExposeData) && !snap.IsEmpty ? snap : null;
        }

        /// <summary>
        /// Applies the card's choice to this colony once. "" = my defaults (the library as it is; with <paramref name="alsoPolicies"/>
        /// the saved policy set too, unless the new-game step already applied it). A profile name: its presets join the library and
        /// become the defaults, its colony settings and policy set are applied to this colony, and the starting colonists get its
        /// default colonist preset. Returns false when the card was already done for this save.
        /// </summary>
        public static bool Apply(string choice, bool alsoPolicies, bool joined)
        {
            if (SmdGameData.startCard == SmdGameData.StartDone) return false;
            SmdGameData.startCard = SmdGameData.StartDone;
            SmdGameData.startChoice = choice ?? "";
            AppliedCount++;
            if (joined || Current.Game == null)
            {
                WBD_Log.Msg("First-start card done (" + (joined ? "joined an existing colony, nothing changed in it" : "no game") + ").");
                return true;
            }
            var comp = SmdRoster.Current;
            try
            {
                if (string.IsNullOrEmpty(choice))
                {
                    if (alsoPolicies && !SmdGameData.policiesAppliedAtStart && ColonyDefaults.ApplyPoliciesToColony(comp))
                        SmdGameData.policiesAppliedAtStart = true;
                    WBD_Log.Msg("First-start card: this colony uses your defaults" + (SmdGameData.policiesAppliedAtStart ? " (with the saved policies)." : "."));
                    return true;
                }
                var p0 = SmdLibraryStore.Library.Profile(choice);
                if (p0 == null) { WBD_Log.Warn("First-start card: the profile \"" + choice + "\" no longer exists; your defaults are used."); return true; }
                var p = p0.Clone();
                // 1. the library: the profile's presets join it and become the defaults (replaced versions are kept as a profile)
                var res = SmdLibraryApi.ApplyProfile(choice, ProfileParts.All, false, true);
                // 2. this colony: straight from the profile, so it gets the profile even when the library could not be written
                var pawnPreset = ReadPawnDefault(p);
                var done = new List<string>();
                if (p.content.colony != null && ColonyDefaults.ApplyColonyToColony(comp, p.content.colony, pawnPreset)) done.Add("WBD_PvColony".Translate());
                if (p.content.policies != null && ColonyDefaults.ApplyPoliciesToColony(comp, p.content.policies, pawnPreset))
                {
                    SmdGameData.policiesAppliedAtStart = true;
                    done.Add("WBD_PvPolicies".Translate());
                }
                if (pawnPreset != null)
                {
                    int n = NewColonistDefaults.ApplyDefaultToStartingColonists(pawnPreset);
                    if (n > 0) done.Add("WBD_CardAppliedPawns".Translate(n));
                }
                string msg = "WBD_CardApplied".Translate(choice) + (done.Count > 0 ? " " + string.Join(", ", done) + "." : "");
                if (!res.ok)
                    msg += " " + "WBD_CardLibraryNotSaved".Translate(res.message ?? "?");
                else if (res.backupProfile != null)
                    msg += " " + "WBD_BackupProfileMade".Translate(res.backupProfile);
                if (!res.missing.IsEmpty) msg += " " + res.missing.Summary();
                LastMessage = msg;
                Messages.Message(msg, res.ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.CautionInput, false);
                WBD_Log.Msg("First-start card: profile \"" + choice + "\" applied to this colony (" + string.Join(", ", done) + "); library "
                    + (res.ok ? "updated (" + res.merged + " entries" + (res.backupProfile != null ? ", replaced versions kept as \"" + res.backupProfile + "\"" : "") + ")" : "NOT updated: " + res.message) + ".");
            }
            catch (Exception e)
            {
                WBD_Log.Warn("First-start card: applying the choice failed (" + e.GetType().Name + ": " + e.Message + "); the colony keeps your defaults.");
            }
            return true;
        }
    }


    /// <summary>
    /// "Start with this profile? It replaces these presets …": Apply (button A, Enter) or "Use my defaults instead" (button B, Escape).
    /// Closed any other way, the colony starts with the player's own defaults (the safe choice: nothing in the library changes).
    /// </summary>
    public sealed class Dialog_SmdReplaceConfirm : Dialog_MessageBox
    {
        readonly string choice; readonly bool alsoPolicies; bool decided;

        public Dialog_SmdReplaceConfirm(string choice, string list, bool alsoPolicies)
            : base("WBD_CardReplaceConfirm".Translate(choice, list, "WBD_BackupProfileName".Translate(choice)), "WBD_CardApplyProfile".Translate(), null,
                "WBD_CardUseMineInstead".Translate(), null)
        {
            this.choice = choice; this.alsoPolicies = alsoPolicies;
            buttonAAction = Profile; buttonBAction = Mine; acceptAction = Profile; cancelAction = Mine;
            forceCatchAcceptAndCancelEventEvenIfUnfocused = true;
        }

        /// <summary>Apply the profile (tests call it like a click).</summary>
        public void Profile() { if (decided) return; decided = true; SmdStartCard.Apply(choice, false, false); }
        /// <summary>Use the player's own defaults instead.</summary>
        public void Mine() { if (decided) return; decided = true; SmdStartCard.Apply("", alsoPolicies, false); }

        public override void PostClose() { base.PostClose(); Mine(); }
    }
}
