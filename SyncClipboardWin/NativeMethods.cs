using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SyncClipboardWin
{
    internal static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int VK_CONTROL = 0x11;
        public const int VK_SHIFT = 0x10;
        public const int VK_MENU = 0x12;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            out IntPtr ppszPath);

        public static readonly Guid FOLDERID_Downloads =
            new Guid("374DE290-123F-4565-9164-39C4925E467B");

        public static readonly Guid FOLDERID_Desktop =
            new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        public static string GetKnownFolderPath(Guid id)
        {
            IntPtr pPath;
            int hr = SHGetKnownFolderPath(id, 0, IntPtr.Zero, out pPath);
            if (hr != 0 || pPath == IntPtr.Zero)
            {
                Exception ex = Marshal.GetExceptionForHR(hr);
                if (ex != null)
                    throw ex;
                throw new InvalidOperationException("无法读取 Windows 已知文件夹路径。");
            }

            try
            {
                return Marshal.PtrToStringUni(pPath) ?? "";
            }
            finally
            {
                Marshal.FreeCoTaskMem(pPath);
            }
        }

        public static string GetWindowClassName(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        public static bool IsModifierDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public static void SendCtrlC()
        {
            INPUT[] inputs = new INPUT[]
            {
                KeyDown((ushort)Keys.ControlKey),
                KeyDown((ushort)Keys.C),
                KeyUp((ushort)Keys.C),
                KeyUp((ushort)Keys.ControlKey)
            };

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT KeyDown(ushort vk)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U = new InputUnion();
            input.U.ki = new KEYBDINPUT();
            input.U.ki.wVk = vk;
            return input;
        }

        private static INPUT KeyUp(ushort vk)
        {
            INPUT input = KeyDown(vk);
            input.U.ki.dwFlags = KEYEVENTF_KEYUP;
            return input;
        }
    }
}
