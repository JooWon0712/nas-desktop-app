using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace NasDesk
{
    internal static class TrayIsolationTests
    {
        internal static bool HasNoNativeIcon(MainWindow window)
        {
            return typeof(MainWindow).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) == null;
        }

        internal static void Run(string output, Action<bool, string> check)
        {
            string directory = Path.Combine(output, "tray-isolation");
            Directory.CreateDirectory(directory);
            MainWindow window = new MainWindow(new ConnectionSettings { Url = "https://tray-isolation.test/" },
                Path.Combine(directory, "profile"), true, Path.Combine(directory, "connection.json"));
            ContextMenuStrip menu = (ContextMenuStrip)typeof(MainWindow)
                .GetField("trayMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            try
            {
                check(HasNoNativeIcon(window) && !window.ActualTrayRegistered && !window.TrayVisible && window.TrayAvailable,
                    "Test windows provide logical tray behavior without constructing a native notification icon");
                check(menu != null && menu.Items.Count > 0 && !menu.IsDisposed,
                    "Test tray menu remains available for local menu and theme checks without a notification icon");

                // Visual previews may reveal a test window or create its HWND.
                // Neither action should allocate an OS notification icon.
                window.Opacity = 1;
                window.ShowInTaskbar = true;
                IntPtr handle = window.Handle;
                check(handle != IntPtr.Zero && HasNoNativeIcon(window) && !window.ActualTrayRegistered && window.TrayAvailable,
                    "Visible preview settings and native window creation cannot register a test notification icon");
            }
            finally { window.Dispose(); }
            check(window.IsDisposed && menu.IsDisposed && HasNoNativeIcon(window)
                && !window.ActualTrayRegistered && !window.TrayAvailable,
                "Test disposal releases its standalone tray menu without constructing or registering an OS icon");
        }
    }
}
