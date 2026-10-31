using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NasDesk
{
    internal static class WindowLifecycleTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string directory = Path.Combine(output, "window-lifecycle");
            Directory.CreateDirectory(directory);
            string settingsPath = Path.Combine(directory, "connection.json");
            ConnectionSettings settings = new ConnectionSettings { Url = "https://window-lifecycle.test/", Type = "https" };
            settings.Save(settingsPath);
            check(!typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(field => typeof(NotifyIcon).IsAssignableFrom(field.FieldType)),
                "The application window has no notification-icon resource in either normal or test mode");

            using (MainWindow window = new MainWindow(settings, Path.Combine(directory, "profile"), true, settingsPath))
            {
                check(window.ControlBox && window.MinimizeBox && window.MaximizeBox && !window.IsDisposed,
                    "The desktop window retains standard close, minimize and maximize controls");
                bool closing = false, closed = false;
                window.FormClosing += delegate(object sender, FormClosingEventArgs e) { closing = e.CloseReason == CloseReason.UserClosing && !e.Cancel; };
                window.FormClosed += delegate { closed = true; };
                // Creating a hidden HWND exercises the real WM_CLOSE path
                // without showing a window or initializing its web engine.
                IntPtr handle = window.Handle;
                window.Close();
                check(handle != IntPtr.Zero && closing && closed && window.IsDisposed && window.Browser.IsDisposed,
                    "Closing the native main window completes user-close events and disposes its web control");
            }

            using (MainWindow reopened = new MainWindow(ConnectionSettings.Read(settingsPath), Path.Combine(directory, "profile"), true, settingsPath))
            {
                IntPtr handle = reopened.Handle;
                check(handle != IntPtr.Zero && !reopened.IsDisposed && reopened.Address.AddressText == settings.Url,
                    "After a normal close, the saved connection can construct another isolated application window");
                reopened.Close();
                check(reopened.IsDisposed && reopened.Browser.IsDisposed,
                    "A reopened application window also closes completely without hidden background window state");
            }
        }
    }
}
