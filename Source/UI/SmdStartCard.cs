using System;
using System.Collections.Generic;
using System.Linq;
using BetterWorkbenchDefaults.Lib;
using HarmonyLib;
using JBFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// The welcome card of Set my Defaults (M7). On a new colony it asks what the colony starts with: "Use my defaults"
    /// (preselected, the obvious choice) or one of the saved profiles, each with a one-line preview and a missing-mod marker; below it
    /// the three "what happens by itself" choices. When the mod joins an existing save only the second part is shown. The choice is
    /// applied exactly once, when the card closes in any way (Start, Escape, a test runner closing it).
    ///
    /// FW-REQUEST: JB Framework's own welcome card cannot show mod content (profiles), so this mod leaves KitInfo.FirstStartPitch
    /// empty (no framework card) and draws its own card in the same style, reading and writing the framework's "Show welcome cards"
    /// preference through the framework's settings. See FW_requests/SetMyDefaults.md (7).
    /// </summary>
    public sealed class Dialog_SmdStartCard : Window
    {
        public readonly bool Joined;
        /// <summary>"" = my defaults, else a profile name.</summary>
        public string Choice = "";
        /// <summary>Starts from "Apply the saved policies to new games" (off by default: applying a set is the player's decision).</summary>
        public bool AlsoPolicies;
        public bool UseForAll, NeverAgain;
        /// <summary>The player confirmed that the chosen profile may replace presets of the same name (tests set it).</summary>
        public bool ReplaceConfirmed;
        /// <summary>Where the "what happens by itself" strip and the scrolling start choices were drawn last (window content coordinates; tests).</summary>
        public Rect ModesRect, BodyRect, ContentRect;
        bool finished, openLibrary;
        Vector2 scroll; float bodyH = 400f;
        // what the chosen profile would replace, per choice and library revision (not recomputed every frame)
        string replFor; int replRev = -1; List<string> repl = new List<string>();

        public override Vector2 InitialSize => KitUI.Size(880f, 640f, 680f, 460f, 0.94f, 0.92f);

        public Dialog_SmdStartCard(bool joined)
        {
            Joined = joined;
            forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; doCloseX = false; doCloseButton = false;
            closeOnAccept = false; closeOnCancel = false; draggable = false; preventSave = false;
            AlsoPolicies = WBD_Mod.Settings?.policyAutoApplyOnNewGame ?? false;
        }

        /// <summary>Library entries the chosen profile would overwrite with a different version ("Electric stove: Kitchen").</summary>
        public List<string> WouldReplace()
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

        /// <summary>Start (or open the library): when the chosen profile replaces presets of the same name, the player confirms first
        /// and sees which ones (their old versions are kept as a profile anyway).</summary>
        public void TryClose(bool thenLibrary)
        {
            var r = WouldReplace();
            if (r.Count > 0 && !ReplaceConfirmed)
            {
                string list = string.Join(", ", r.Take(12)) + (r.Count > 12 ? " …" : "");
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("WBD_CardReplaceConfirm".Translate(Choice, list, "WBD_BackupProfileName".Translate(Choice)),
                    () => { ReplaceConfirmed = true; openLibrary = thenLibrary; Close(); }));
                return;
            }
            openLibrary = thenLibrary;
            Close();
        }

        public override void PreClose() { base.PreClose(); Finish(); }

        public override void PostClose()
        {
            base.PostClose();
            if (openLibrary) Dialog_SmdLibrary.Open(Joined ? SmdLibraryUi.PageLibrary : SmdLibraryUi.PageProfiles);
        }

        /// <summary>Applies the choice once (also when the card is closed by other means).</summary>
        public void Finish()
        {
            if (finished) return;
            finished = true;
            var k = WBD_Mod.K;
            if (UseForAll && k != null && k.HasSaveProfile) k.SaveAsTemplate();
            if (NeverAgain) SmdStartCard.SetWelcomeCards(false);
            SmdStartCard.Apply(Joined ? "" : Choice, !Joined && Choice == "" && AlsoPolicies, Joined);
        }

        public override void DoWindowContents(Rect inRect) => SmdUi.Probe("card", () => Draw(inRect));

        void Draw(Rect inRect)
        {
            float y = inRect.y;
            // header band
            var mark = ContentFinder<Texture2D>.Get("UI/WBD_Defaults", false);
            float tx = 20f + (mark != null ? 76f : 0f);
            string title = Joined ? "WBD_CardTitleJoined".Translate() : "WBD_CardTitle".Translate();
            string sub = Joined ? "WBD_CardPitchJoined".Translate() : "WBD_CardPitch".Translate();
            float tw = inRect.width - tx - 20f;
            float th = SmdUi.TextH(title, tw, GameFont.Medium), sh = SmdUi.TextH(sub, tw, GameFont.Small);
            float headH = Mathf.Max(mark != null ? 96f : 80f, 16f + th + 6f + sh + 16f);
            var hr = new Rect(inRect.x, y, inRect.width, headH);
            Widgets.DrawBoxSolid(hr, new Color(0.13f, 0.12f, 0.11f));
            Widgets.DrawBoxSolid(new Rect(hr.x, hr.yMax - 3f, hr.width, 3f), SmdUi.Accent);
            if (mark != null) GUI.DrawTexture(new Rect(hr.x + 16f, hr.y + (headH - 64f) / 2f, 64f, 64f), mark);
            Text.Font = GameFont.Medium; GUI.color = new Color(1f, 0.96f, 0.9f);
            Widgets.Label(new Rect(hr.x + tx, hr.y + 16f, tw, th + 2f), title);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(hr.x + tx, hr.y + 16f + th + 6f, tw, sh + 2f), sub);
            GUI.color = Color.white;
            y = hr.yMax + 12f;

            // footer
            float footH = 80f;
            var br = new Rect(inRect.x, inRect.yMax - footH, inRect.width, footH);
            Widgets.DrawBoxSolid(new Rect(br.x, br.y, br.width, 1f), SmdUi.Line);
            string start = "WBD_CardStart".Translate(), lib = "WBD_CardOpenLibrary".Translate();
            float startW = Mathf.Clamp(SmdUi.TextW(start, GameFont.Small) + 60f, 160f, 280f), libW = Mathf.Clamp(SmdUi.TextW(lib, GameFont.Small) + 40f, 160f, 300f);
            float checksW = br.width - startW - libW - 30f;
            SmdUi.Check(br.x, br.y + 8f, checksW, "WBD_CardUseForAll".Translate(), ref UseForAll, false, "WBD_CardUseForAllTip".Translate());
            SmdUi.Check(br.x, br.y + 42f, checksW, "WBD_CardNeverAgain".Translate(), ref NeverAgain, false, "WBD_CardNeverAgainTip".Translate());
            if (Widgets.ButtonText(new Rect(br.xMax - startW - libW - 10f, br.y + 22f, libW, 36f), lib)) TryClose(true);
            var sr = new Rect(br.xMax - startW, br.y + 22f, startW, 36f);
            Widgets.DrawBoxSolid(sr, SmdUi.AccentSoft);
            if (Widgets.ButtonText(sr, start)) TryClose(false);

            if (Joined)
            {
                // the mod joined an existing colony: only the "what happens by itself" choice
                ModesRect = new Rect(inRect.x, y, inRect.width, ModesHeight(inRect.width, false)); ContentRect = inRect; BodyRect = default;
                try { Modes(ModesRect, false); }
                finally { SmdUi.Reset(); }
                return;
            }

            // "what happens by itself" stays in view above the footer (compact on small screens); the start choices scroll above it
            bool compact = inRect.height < 600f;
            float modesH = ModesHeight(inRect.width, compact);
            var mr = new Rect(inRect.x, br.y - 6f - modesH, inRect.width, modesH);
            ModesRect = mr; ContentRect = inRect;
            try { Modes(mr, compact); }
            finally { SmdUi.Reset(); }

            // body
            var outer = new Rect(inRect.x, y, inRect.width, mr.y - y - 6f);
            BodyRect = outer;
            if (outer.height < 40f) return;
            var view = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(bodyH, outer.height - 2f));
            Widgets.BeginScrollView(outer, ref scroll, view);
            try { bodyH = Body(view.width) + 6f; }
            finally { Widgets.EndScrollView(); SmdUi.Reset(); }
        }

        /// <summary>Height of the "what happens by itself" strip (full: label + description per card; compact: label only, the
        /// description as tooltip).</summary>
        float ModesHeight(float w, bool compact)
        {
            var modes = WBD_Mod.Modes;
            int perRow = w >= (compact ? 420f : 640f) ? 3 : 1;
            float gap = 12f, pw = (w - gap * (perRow - 1)) / perRow;
            float cardH = CardH(pw, compact);
            float h = SectionH(w) + ((modes.Count + perRow - 1) / perRow) * (cardH + gap) - gap + 6f;
            if (!compact) h += SmdUi.TextH("WBD_CardHint".Translate(), w, GameFont.Tiny) + 4f;
            return h;
        }

        static float SectionH(float w) => 34f;

        float CardH(float pw, bool compact)
        {
            float cardH = compact ? 36f : 64f;
            foreach (var m in WBD_Mod.Modes)
                cardH = Mathf.Max(cardH, compact
                    ? 10f + SmdUi.TextH(m.Label, pw - 24f, GameFont.Small) + 8f
                    : 14f + SmdUi.TextH(m.Label, pw - 24f, GameFont.Small) + 4f + SmdUi.TextH(m.Desc, pw - 24f, GameFont.Tiny) + 12f);
            return cardH;
        }

        void Modes(Rect area, bool compact)
        {
            float w = area.width, y = area.y;
            SmdUi.Section(area.x, y, w, "WBD_CardModes".Translate());
            y += SectionH(w);
            var k = WBD_Mod.K;
            var modes = WBD_Mod.Modes;
            int perRow = w >= (compact ? 420f : 640f) ? 3 : 1;
            float gap = 12f, pw = (w - gap * (perRow - 1)) / perRow, cardH = CardH(pw, compact);
            for (int i = 0; i < modes.Count; i++)
            {
                var m = modes[i];
                var r = new Rect(area.x + (i % perRow) * (pw + gap), y + (i / perRow) * (cardH + gap), pw, cardH);
                bool sel = k?.SaveProfile != null && m.Matches(k.SaveProfile);
                Widgets.DrawBoxSolid(r, sel ? SmdUi.AccentSoft : SmdUi.CardBg);
                GUI.color = sel ? SmdUi.Accent : SmdUi.Line; Widgets.DrawBox(r, sel ? 2 : 1); GUI.color = Color.white;
                if (Mouse.IsOver(r) && !sel) Widgets.DrawHighlight(r);
                float lh = SmdUi.TextH(m.Label, r.width - 24f, GameFont.Small);
                SmdUi.Para(r.x + 12f, r.y + (compact ? (cardH - lh) / 2f : 10f), r.width - 24f, m.Label, sel ? SmdUi.Accent : Color.white);
                if (compact) TooltipHandler.TipRegion(r, m.Desc);
                else SmdUi.Para(r.x + 12f, r.y + 14f + lh, r.width - 24f, m.Desc, SmdUi.Grey, GameFont.Tiny);
                if (Widgets.ButtonInvisible(r) && k?.SaveProfile != null) m.Apply(k.SaveProfile);
            }
            y += ((modes.Count + perRow - 1) / perRow) * (cardH + gap) - gap + 6f;
            if (!compact) SmdUi.Para(area.x, y, w, "WBD_CardHint".Translate(), SmdUi.DimGrey, GameFont.Tiny);
        }

        float Body(float w)
        {
            float y = 0f;
            bool profilesOn = SmdSections.On(SmdSections.Library);
            if (!Joined)
            {
                y += SmdUi.Section(0f, y, w, "WBD_CardStartWith".Translate());
                var lib = SmdLibraryStore.Content;
                string mine = lib.IsEmpty ? "WBD_CardMineEmpty".Translate().ToString() : SmdPreview.OneLine(lib);
                y += Option(y, w, "", "WBD_CardMine".Translate(), "WBD_CardRecommended".Translate(), mine, SmdLibraryApi.MissingInLibrary()) + 8f;
                if (profilesOn)
                {
                    var profiles = SmdLibraryStore.Library.profiles.OrderBy(p => p.name).ToList();
                    foreach (var p in profiles)
                        y += Option(y, w, p.name, p.name, null, SmdPreview.OneLine(p.content), SmdLibraryApi.MissingFor(p.content)) + 8f;
                    if (profiles.Count == 0)
                        y += SmdUi.Para(4f, y, w - 8f, "WBD_CardNoProfiles".Translate(), SmdUi.DimGrey, GameFont.Tiny) + 8f;
                }
                if (Choice == "" && PolicyDefaultsStore.HasSet() && !SmdGameData.policiesAppliedAtStart && SmdSections.On(SmdSections.Policies))
                    y += SmdUi.Check(4f, y, w - 8f, "WBD_CardAlsoPolicies".Translate(), ref AlsoPolicies, false, "WBD_CardAlsoPoliciesTip".Translate()) + 8f;
                y += 8f;
            }

            return y;
        }

        /// <summary>One choice row (radio card): title (+ chip), then the one-line preview with the missing-mod note after it.
        /// Compact on purpose, so the "what happens by itself" part stays in view on small screens. Returns its height.</summary>
        float Option(float y, float w, string id, string title, string chip, string line, MissingReport missing)
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
            float h = 8f + 24f + SmdUi.TextH(line, iw, GameFont.Tiny) + 8f;
            var r = new Rect(0f, y, w, h);
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
    }

    /// <summary>When the card opens, and what its choice does (exactly once per save).</summary>
    public static class SmdStartCard
    {
        /// <summary>Tests set this to keep the card from opening by itself.</summary>
        public static bool Suppressed;
        /// <summary>How often a choice was applied this session (tests: exactly once).</summary>
        public static int AppliedCount;
        /// <summary>The message the last applied profile showed (tests).</summary>
        public static string LastMessage;

        static PawnConfigSnapshot ReadPawnDefault(SmdProfile p)
        {
            var sp = p?.content?.pawns?.DefaultPreset;
            if (sp?.config == null) return null;
            var snap = new PawnConfigSnapshot();
            return SmdScribe.Read(sp.config, snap.ExposeData) && !snap.IsEmpty ? snap : null;
        }
        public const float Delay = 1.5f;
        static float readySince = -1f;

        public static bool WelcomeCards
        {
            get
            {
                try { return Framework.Of<FrameworkMod>()?.GetBool("jbf.welcomeCards") ?? true; }
                catch { return true; }
            }
        }

        public static void SetWelcomeCards(bool on)
        {
            try
            {
                var fw = Framework.Of<FrameworkMod>();
                if (fw == null) return;
                fw.Set("jbf.welcomeCards", on);
                fw.WriteSettings();
            }
            catch (Exception e) { WBD_Log.Warn("Could not change the welcome card preference: " + e.Message); }
        }

        /// <summary>True when a card is due now (ignoring the short delay).</summary>
        public static bool Due()
        {
            if (Suppressed) return false;
            int st = SmdGameData.startCard;
            if (st != SmdGameData.StartNewColony && st != SmdGameData.StartJoined) return false;
            var k = WBD_Mod.K;
            if (k == null || k.Blocked) return false;
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || Find.WindowStack == null) return false;
            if (LongEventHandler.AnyEventNowOrWaiting) return false;
            foreach (var w in Find.WindowStack.Windows)
                if (w is Page || w is Dialog_SmdStartCard || w.GetType().Name == "Dialog_KitFirstStartCard") return false;
            return true;
        }

        internal static void OnGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (!Due()) { readySince = -1f; return; }
            float now = Time.realtimeSinceStartup;
            if (readySince < 0f) { readySince = now; return; }
            if (now - readySince < Delay) return;
            readySince = -1f;
            bool joined = SmdGameData.startCard == SmdGameData.StartJoined;
            if (!WelcomeCards)
            {
                // cards switched off for every JB mod: the colony keeps what the new-game step applied, quietly
                Apply("", false, joined);
                WBD_Log.Msg("Welcome cards are switched off: this colony uses your defaults.");
                return;
            }
            Find.WindowStack.Add(new Dialog_SmdStartCard(joined));
        }

        /// <summary>Tests: open the card now (ignores delay and state).</summary>
        public static Dialog_SmdStartCard OpenNow(bool joined)
        {
            var d = new Dialog_SmdStartCard(joined);
            Find.WindowStack.Add(d);
            return d;
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
            var comp = Current.Game.GetComponent<PawnDefaultsGameComponent>();
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

    /// <summary>Opens the welcome card once the map is on screen (same moment as JB Framework's cards).</summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class Patch_UIRoot_Play_StartCard
    {
        static bool failed;

        public static void Postfix()
        {
            if (failed) return;
            try { SmdStartCard.OnGUI(); }
            catch (Exception e) { failed = true; WBD_Log.Warn("The welcome card could not be opened (" + e.Message + "); your defaults are used."); }
        }
    }
}
