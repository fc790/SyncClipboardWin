using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SyncClipboardWin
{
    public static class ClipboardHelper
    {
        private static int _internalClipboardDepth;

        public static bool IsInternalClipboardOperation
        {
            get { return Thread.VolatileRead(ref _internalClipboardDepth) > 0; }
        }

        public static IDisposable BeginInternalClipboardOperation()
        {
            Interlocked.Increment(ref _internalClipboardDepth);
            return new Scope();
        }

        public static string TryGetSelectedTextWithCopyFallback()
        {
            using (BeginInternalClipboardOperation())
            {
                ClipboardSnapshot snapshot = ClipboardSnapshot.Capture();
                IntPtr foreground = NativeMethods.GetForegroundWindow();

                try
                {
                    WaitForHotkeyModifiersReleased(2000);

                    string marker = "__SyncClipboardWin_probe_" +
                                    Guid.NewGuid().ToString("N") + "__";

                    Clipboard.SetText(marker);
                    uint markerSequence = NativeMethods.GetClipboardSequenceNumber();

                    if (foreground != IntPtr.Zero)
                        NativeMethods.SetForegroundWindow(foreground);

                    Thread.Sleep(30);
                    SendKeys.SendWait("^c");

                    System.Diagnostics.Stopwatch sw =
                        System.Diagnostics.Stopwatch.StartNew();

                    while (sw.ElapsedMilliseconds < 400)
                    {
                        Thread.Sleep(20);

                        uint seq = NativeMethods.GetClipboardSequenceNumber();
                        if (seq == markerSequence)
                            continue;

                        if (Clipboard.ContainsText())
                        {
                            string copied = Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(copied) && copied != marker)
                                return copied;
                        }

                        if (Clipboard.ContainsFileDropList() || Clipboard.ContainsImage())
                            return null;
                    }
                }
                catch { }
                finally
                {
                    snapshot.Restore();
                    Thread.Sleep(30);
                }

                return null;
            }
        }

        private static void WaitForHotkeyModifiersReleased(int timeoutMs)
        {
            System.Diagnostics.Stopwatch sw =
                System.Diagnostics.Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!NativeMethods.IsModifierDown(NativeMethods.VK_CONTROL) &&
                    !NativeMethods.IsModifierDown(NativeMethods.VK_SHIFT) &&
                    !NativeMethods.IsModifierDown(NativeMethods.VK_MENU) &&
                    !NativeMethods.IsModifierDown(NativeMethods.VK_LWIN) &&
                    !NativeMethods.IsModifierDown(NativeMethods.VK_RWIN))
                {
                    return;
                }

                Thread.Sleep(10);
            }
        }

        public static void PasteText(string text)
        {
            using (BeginInternalClipboardOperation())
            {
                Clipboard.SetText(text);
                Thread.Sleep(40);
                SendKeys.SendWait("^v");
                Thread.Sleep(30);
            }
        }

        public static void PasteFile(string path)
        {
            using (BeginInternalClipboardOperation())
            {
                System.Collections.Specialized.StringCollection collection =
                    new System.Collections.Specialized.StringCollection();

                collection.Add(path);
                Clipboard.SetFileDropList(collection);
                Thread.Sleep(50);
                SendKeys.SendWait("^v");
                Thread.Sleep(30);
            }
        }

        private sealed class Scope : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    Interlocked.Decrement(ref _internalClipboardDepth);
            }
        }

        private sealed class ClipboardSnapshot
        {
            private string _text;
            private string[] _files;
            private Bitmap _image;
            private string _html;
            private bool _hadAny;

            public static ClipboardSnapshot Capture()
            {
                ClipboardSnapshot s = new ClipboardSnapshot();

                try
                {
                    if (Clipboard.ContainsText())
                    {
                        s._text = Clipboard.GetText();
                        s._hadAny = true;
                    }
                }
                catch { }

                try
                {
                    if (Clipboard.ContainsFileDropList())
                    {
                        s._files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                        s._hadAny = true;
                    }
                }
                catch { }

                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        Image image = Clipboard.GetImage();
                        if (image != null)
                        {
                            s._image = new Bitmap(image);
                            s._hadAny = true;
                        }
                    }
                }
                catch { }

                try
                {
                    if (Clipboard.ContainsData(DataFormats.Html))
                    {
                        s._html = Clipboard.GetData(DataFormats.Html) as string;
                        if (s._html != null)
                            s._hadAny = true;
                    }
                }
                catch { }

                return s;
            }

            public void Restore()
            {
                try
                {
                    if (!_hadAny)
                    {
                        Clipboard.Clear();
                        return;
                    }

                    DataObject data = new DataObject();

                    if (_text != null)
                        data.SetText(_text);

                    if (_files != null)
                    {
                        System.Collections.Specialized.StringCollection c =
                            new System.Collections.Specialized.StringCollection();
                        c.AddRange(_files);
                        data.SetFileDropList(c);
                    }

                    if (_image != null)
                        data.SetImage(_image);

                    if (_html != null)
                        data.SetData(DataFormats.Html, _html);

                    Clipboard.SetDataObject(data, true);
                }
                catch { }
            }
        }
    }
}
