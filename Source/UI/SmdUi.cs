using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Small drawing helpers shared by the library pages (inside the framework's settings window) and this mod's own windows
    /// (library window, first-start card). Every helper returns the height it used, so pages stack rows without fixed heights and
    /// long (German) texts wrap instead of being cut off.
    /// </summary>
    internal static class SmdUi
    {
        /// <summary>The mod's accent (same as KitInfo.Theme.Accent). FW-REQUEST: KitStyle.Accent outside the framework window is
        /// Hearth &amp; Harbor amber (KitMod.Drawing is internal); a public KitStyle.AccentOf(KitMod) would let us drop this copy.</summary>
        public static readonly Color Accent = new Color(0.47f, 0.72f, 0.45f);
        public static Color AccentSoft => new Color(Accent.r, Accent.g, Accent.b, 0.16f);
        public static readonly Color Grey = new Color(0.72f, 0.72f, 0.72f);
        public static readonly Color DimGrey = new Color(0.56f, 0.56f, 0.56f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.15f);
        public static readonly Color Warn = new Color(0.95f, 0.62f, 0.35f);
        public static readonly Color Bad = new Color(0.88f, 0.42f, 0.38f);
        public static readonly Color CardBg = new Color(1f, 1f, 1f, 0.035f);
        public const float RowH = 28f, Gap = 6f, BtnH = 28f;

        /// <summary>Tests: number of draws and the last exception per page id ("library", "profiles", "share", "window", "card").</summary>
        public static readonly Dictionary<string, int> Draws = new Dictionary<string, int>();
        public static readonly Dictionary<string, string> Errors = new Dictionary<string, string>();

        /// <summary>Runs a page draw, counts it and remembers an exception (then rethrows, so the framework fence shows its box).</summary>
        public static void Probe(string id, Action draw)
        {
            Draws.TryGetValue(id, out int n);
            Draws[id] = n + 1;
            try { draw(); }
            catch (Exception e)
            {
                Errors[id] = e.GetType().Name + ": " + e.Message;
                Reset();
                throw;
            }
            finally { Reset(); }
        }

        public static void Reset()
        {
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
        }

        public static float TextH(string text, float w, GameFont font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var f = Text.Font; Text.Font = font;
            float h = Text.CalcHeight(text, Mathf.Max(20f, w));
            Text.Font = f;
            return h;
        }

        public static float TextW(string text, GameFont font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var f = Text.Font; Text.Font = font;
            bool ww = Text.WordWrap; Text.WordWrap = false;
            float w = Text.CalcSize(text).x;
            Text.WordWrap = ww; Text.Font = f;
            return w;
        }

        /// <summary>A wrapped paragraph; returns its height.</summary>
        public static float Para(float x, float y, float w, string text, Color c, GameFont font = GameFont.Small)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float h = TextH(text, w, font);
            var f = Text.Font; Text.Font = font; GUI.color = c;
            Widgets.Label(new Rect(x, y, w, h + 2f), text);
            GUI.color = Color.white; Text.Font = f;
            return h;
        }

        /// <summary>One line, cut with "..." when too long (the full text as tooltip). Returns the height.</summary>
        public static float Line1(Rect r, string text, Color c, GameFont font = GameFont.Small)
        {
            var f = Text.Font; Text.Font = font; GUI.color = c;
            bool ww = Text.WordWrap; Text.WordWrap = false;
            string shown = text.Truncate(r.width);
            Widgets.Label(r, shown);
            Text.WordWrap = ww; GUI.color = Color.white; Text.Font = f;
            if (shown != text) TooltipHandler.TipRegion(r, text);
            return r.height;
        }

        /// <summary>Section header: accent caps text and a thin rule. Returns 32.</summary>
        public static float Section(float x, float y, float w, string text)
        {
            var f = Text.Font; Text.Font = GameFont.Small; GUI.color = Accent;
            bool ww = Text.WordWrap; Text.WordWrap = false;
            Widgets.Label(new Rect(x, y, w, 24f), (text ?? "").ToUpper().Truncate(w));
            Text.WordWrap = ww;
            GUI.color = Line; Widgets.DrawLineHorizontal(x, y + 24f, w); GUI.color = Color.white; Text.Font = f;
            return 32f;
        }

        public static float BtnW(string label, float min = 90f) => Mathf.Max(min, TextW(label, GameFont.Small) + 24f);

        /// <summary>A button sized to its label.</summary>
        public static bool Button(float x, float y, string label, out float width, bool active = true, string tip = null, float min = 90f)
        {
            width = BtnW(label, min);
            var r = new Rect(x, y, width, BtnH);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonText(r, label, true, true, active) && active;
        }

        /// <summary>One button of a <see cref="Buttons"/> row.</summary>
        public sealed class Btn
        {
            public string label, tip; public Action act; public bool active = true; public bool accent;
            public Btn(string label, Action act, bool active = true, string tip = null, bool accent = false)
            { this.label = label; this.act = act; this.active = active; this.tip = tip; this.accent = accent; }
        }

        /// <summary>Buttons left to right, wrapping to a new line when the width runs out. Returns the height used.</summary>
        public static float Buttons(float x, float y, float w, IList<Btn> buttons, float min = 90f)
        {
            float cx = x, cy = y;
            Action clicked = null;
            foreach (var b in buttons)
            {
                if (b == null) continue;
                float bw = Mathf.Min(BtnW(b.label, min), w);
                if (cx > x && cx + bw > x + w) { cx = x; cy += BtnH + Gap; }
                var r = new Rect(cx, cy, bw, BtnH);
                if (b.accent && b.active) Widgets.DrawBoxSolid(r, AccentSoft);
                if (!string.IsNullOrEmpty(b.tip)) TooltipHandler.TipRegion(r, b.tip);
                var f = Text.Font; Text.Font = GameFont.Small;
                if (Widgets.ButtonText(r, b.label.Truncate(bw - 8f), true, true, b.active) && b.active) clicked = b.act;
                Text.Font = f;
                cx += bw + Gap;
            }
            clicked?.Invoke();
            return cy - y + BtnH;
        }

        /// <summary>A small coloured marker ("needs X (not active)"); wraps when long. Returns its height.</summary>
        public static float Marker(float x, float y, float w, string text, Color c, string tip)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float tw = Mathf.Min(w - 12f, TextW(text, GameFont.Tiny) + 2f);
            float h = TextH(text, tw, GameFont.Tiny) + 4f;
            var r = new Rect(x, y, tw + 12f, h);
            Widgets.DrawBoxSolid(r, new Color(c.r, c.g, c.b, 0.16f));
            GUI.color = new Color(c.r, c.g, c.b, 0.7f); Widgets.DrawBox(r); GUI.color = Color.white;
            var f = Text.Font; Text.Font = GameFont.Tiny; GUI.color = c;
            Widgets.Label(new Rect(x + 6f, y + 2f, tw + 2f, h), text);
            GUI.color = Color.white; Text.Font = f;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return h;
        }

        /// <summary>Scrolling area: the body draws at (0,0) with the given width and returns its height.</summary>
        public sealed class Scroll { public Vector2 pos; public float height = 400f; }

        public static void Scrolled(Rect area, Scroll s, Func<float, float> body)
        {
            Reset();
            float w = area.width - 18f;
            var view = new Rect(0f, 0f, w, Mathf.Max(s.height, area.height - 2f));
            Widgets.BeginScrollView(area, ref s.pos, view);
            try { s.height = body(w) + 16f; }
            finally { Widgets.EndScrollView(); Reset(); }
        }

        /// <summary>A checkbox with a label that wraps; returns the height.</summary>
        public static float Check(float x, float y, float w, string label, ref bool on, bool disabled = false, string tip = null)
        {
            float lh = TextH(label, w - 34f, GameFont.Small);
            float h = Mathf.Max(24f, lh);
            var r = new Rect(x, y, w, h);
            if (!disabled) Widgets.DrawHighlightIfMouseover(r);
            GUI.color = disabled ? DimGrey : Color.white;
            Widgets.Label(new Rect(x + 30f, y + Mathf.Max(0f, (24f - lh) / 2f), w - 34f, lh + 2f), label);
            GUI.color = Color.white;
            Widgets.Checkbox(new Vector2(x, y + (h - 24f) / 2f), ref on, 24f, disabled);
            if (!disabled && Widgets.ButtonInvisible(new Rect(x + 30f, y, w - 30f, h))) on = !on;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            return h;
        }

        /// <summary>Copies text to the clipboard. False when the system clipboard is not available.</summary>
        public static bool Copy(string text)
        {
            try { GUIUtility.systemCopyBuffer = text ?? ""; return true; }
            catch (Exception e) { WBD_Log.Warn("Copy to clipboard failed: " + e.Message); return false; }
        }

        public static string Paste()
        {
            try { return GUIUtility.systemCopyBuffer ?? ""; }
            catch (Exception e) { WBD_Log.Warn("Paste from clipboard failed: " + e.Message); return ""; }
        }
    }
}
