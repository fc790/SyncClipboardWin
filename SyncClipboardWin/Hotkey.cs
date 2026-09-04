using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SyncClipboardWin
{
    public struct Hotkey
    {
        public uint Modifiers;
        public Keys Key;

        public Hotkey(uint modifiers, Keys key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = new Hotkey();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            uint mods = NativeMethods.MOD_NOREPEAT;
            Keys key = Keys.None;

            string[] parts = text.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string partValue in parts)
            {
                string raw = partValue.Trim();

                if (raw.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_CONTROL;
                }
                else if (raw.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_SHIFT;
                }
                else if (raw.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_ALT;
                }
                else if (raw.Equals("Win", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_WIN;
                }
                else
                {
                    Keys parsed;
                    if (!Enum.TryParse<Keys>(raw, true, out parsed))
                        return false;
                    key = parsed;
                }
            }

            if (key == Keys.None)
                return false;

            hotkey = new Hotkey(mods, key);
            return true;
        }

        public static string FromKeyEvent(KeyEventArgs e)
        {
            List<string> parts = new List<string>();

            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");

            Keys k = e.KeyCode;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu)
                return string.Join("+", parts.ToArray());

            parts.Add(k.ToString());
            return string.Join("+", parts.ToArray());
        }
    }
}
