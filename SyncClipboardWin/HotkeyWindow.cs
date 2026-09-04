using System;
using System.Windows.Forms;

namespace SyncClipboardWin
{
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> HotkeyPressed;
        public event EventHandler ClipboardUpdated;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
            NativeMethods.AddClipboardFormatListener(Handle);
        }

        public bool Register(int id, Hotkey hotkey)
        {
            return NativeMethods.RegisterHotKey(Handle, id, hotkey.Modifiers, (uint)hotkey.Key);
        }

        public void Unregister(int id)
        {
            NativeMethods.UnregisterHotKey(Handle, id);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                Action<int> handler = HotkeyPressed;
                if (handler != null)
                    handler(m.WParam.ToInt32());
            }
            else if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                EventHandler handler = ClipboardUpdated;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            Unregister(1);
            Unregister(2);
            DestroyHandle();
        }
    }
}
