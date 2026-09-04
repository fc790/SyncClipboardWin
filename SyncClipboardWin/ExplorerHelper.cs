using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SyncClipboardWin
{
    public static class ExplorerHelper
    {
        public static bool TryGetActiveExplorer(
            out string currentDirectory,
            out List<string> selectedPaths)
        {
            currentDirectory = null;
            selectedPaths = new List<string>();

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return false;

            string cls = NativeMethods.GetWindowClassName(foreground);
            bool isDesktop =
                foreground == NativeMethods.GetShellWindow() ||
                cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase);

            if (isDesktop)
            {
                currentDirectory = AppSettings.DesktopDirectory;
                selectedPaths = TryGetDesktopSelectedPaths();
                return true;
            }

            object shellObject = null;
            object windowsObject = null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                    return false;

                shellObject = Activator.CreateInstance(shellType);
                dynamic shell = shellObject;
                windowsObject = shell.Windows();
                dynamic windows = windowsObject;

                int count = Convert.ToInt32(windows.Count);
                for (int wi = 0; wi < count; wi++)
                {
                    object windowObject = null;
                    try
                    {
                        windowObject = windows.Item(wi);
                        dynamic window = windowObject;

                        long hwnd = Convert.ToInt64(window.HWND);
                        if (new IntPtr(hwnd) != foreground)
                            continue;

                        string url = Convert.ToString(window.LocationURL) ?? "";
                        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                            currentDirectory = new Uri(url).LocalPath;

                        dynamic doc = window.Document;
                        dynamic items = doc.SelectedItems();
                        int itemCount = Convert.ToInt32(items.Count);

                        for (int i = 0; i < itemCount; i++)
                        {
                            dynamic item = items.Item(i);
                            string path = Convert.ToString(item.Path) ?? "";
                            if (!string.IsNullOrWhiteSpace(path))
                                selectedPaths.Add(path);
                        }

                        return !string.IsNullOrWhiteSpace(currentDirectory);
                    }
                    catch { }
                    finally
                    {
                        ReleaseCom(windowObject);
                    }
                }
            }
            catch { }
            finally
            {
                ReleaseCom(windowsObject);
                ReleaseCom(shellObject);
            }

            return false;
        }

        private static List<string> TryGetDesktopSelectedPaths()
        {
            List<string> result = new List<string>();
            object shell = null;
            object windows = null;
            object disp = null;
            object browserObj = null;
            object viewObj = null;
            object itemsObj = null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                    return result;

                shell = Activator.CreateInstance(shellType);
                if (shell == null)
                    return result;

                dynamic app = shell;
                windows = app.Windows();

                const int SWC_DESKTOP = 8;
                const int SWFO_NEEDDISPATCH = 1;
                int hwnd = 0;

                dynamic shellWindows = windows;
                disp = shellWindows.FindWindowSW(
                    Type.Missing,
                    Type.Missing,
                    SWC_DESKTOP,
                    ref hwnd,
                    SWFO_NEEDDISPATCH);

                IServiceProvider serviceProvider = disp as IServiceProvider;
                if (serviceProvider == null)
                    return result;

                Guid sidTopLevelBrowser =
                    new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");

                browserObj = serviceProvider.QueryService(
                    sidTopLevelBrowser,
                    typeof(IShellBrowser).GUID);

                IShellBrowser browser = browserObj as IShellBrowser;
                if (browser == null)
                    return result;

                viewObj = browser.QueryActiveShellView();

                IFolderView view = viewObj as IFolderView;
                if (view == null)
                    return result;

                Guid iidArray = typeof(IShellItemArray).GUID;
                int hr = view.Items(SVGIO.SVGIO_SELECTION, iidArray, out itemsObj);

                IShellItemArray array = itemsObj as IShellItemArray;
                if (hr != 0 || array == null)
                    return result;

                uint count = array.GetCount();
                for (uint i = 0; i < count; i++)
                {
                    IShellItem item = array.GetItemAt(i);
                    try
                    {
                        string path = item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH);
                        if (!string.IsNullOrWhiteSpace(path) &&
                            (File.Exists(path) || Directory.Exists(path)))
                        {
                            result.Add(path);
                        }
                    }
                    catch { }
                    finally
                    {
                        ReleaseCom(item);
                    }
                }
            }
            catch { }
            finally
            {
                ReleaseCom(itemsObj);
                ReleaseCom(viewObj);
                ReleaseCom(browserObj);
                ReleaseCom(disp);
                ReleaseCom(windows);
                ReleaseCom(shell);
            }

            return result;
        }

        private static void ReleaseCom(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try { Marshal.FinalReleaseComObject(value); }
                catch { }
            }
        }

        [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryService(
                [MarshalAs(UnmanagedType.LPStruct)] Guid service,
                [MarshalAs(UnmanagedType.LPStruct)] Guid riid);
        }

        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void _VtblGap1_12();

            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryActiveShellView();
        }

        [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView
        {
            void _VtblGap1_5();

            [PreserveSig]
            int Items(
                SVGIO uFlags,
                Guid riid,
                [MarshalAs(UnmanagedType.IUnknown)] out object items);
        }

        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object BindToHandler(
                System.Runtime.InteropServices.ComTypes.IBindCtx pbc,
                [MarshalAs(UnmanagedType.LPStruct)] Guid bhid,
                [MarshalAs(UnmanagedType.LPStruct)] Guid riid);

            IShellItem GetParent();

            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetDisplayName(SIGDN sigdnName);
        }

        [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void _VtblGap1_4();
            uint GetCount();
            IShellItem GetItemAt(uint dwIndex);
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000
        }

        [Flags]
        private enum SVGIO : uint
        {
            SVGIO_SELECTION = 0x00000001
        }
    }
}
