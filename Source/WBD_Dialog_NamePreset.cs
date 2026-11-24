using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>Tiny modal text-entry dialog for naming / renaming a preset.</summary>
    public class WBD_Dialog_NamePreset : Window
    {
        private string _name;
        private readonly string _title;
        private readonly Action<string> _onAccept;
        private bool _focused;

        public override Vector2 InitialSize => new Vector2(360f, 170f);

        public WBD_Dialog_NamePreset(string title, string initial, Action<string> onAccept)
        {
            _title = title;
            _name = initial ?? "";
            _onAccept = onAccept;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), _title);

            GUI.SetNextControlName("WBD_NameField");
            _name = Widgets.TextField(new Rect(0f, 36f, inRect.width, 32f), _name);
            if (!_focused)
            {
                GUI.FocusControl("WBD_NameField");
                _focused = true;
            }

            bool enter = Event.current.type == EventType.KeyDown
                         && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            float by = inRect.height - 34f;
            float bw = (inRect.width - 10f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, by, bw, 32f), "OK".Translate()) || enter)
            {
                Accept();
                Event.current.Use();
            }
            if (Widgets.ButtonText(new Rect(bw + 10f, by, bw, 32f), "CancelButton".Translate()))
                Close();
        }

        private void Accept()
        {
            var trimmed = _name?.Trim();
            if (trimmed.NullOrEmpty())
            {
                Messages.Message("WBD_PresetNameEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Close();
            _onAccept?.Invoke(trimmed);
        }
    }
}
