using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class BrowserTabShortcutTests
    {
        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string fixture, Action<bool, string> check)
        {
            HashSet<MainWindow> originalTabs = new HashSet<MainWindow>(window.BrowserTabs);
            string source = core.Source;
            string id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Action<MainWindow> prepare = delegate(MainWindow tab)
            {
                tab.Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("nas-desk.test", fixture,
                    CoreWebView2HostResourceAccessKind.DenyCors);
            };
            window.BrowserTabPrepared += prepare;
            try
            {
                await core.ExecuteScriptAsync("window.shortcutFixtureMarker='home-kept';window.open('/photos.html','shortcut_photos_" + id + "');void(0);");
                MainWindow photos = await NewTab(window, originalTabs, "Shortcut Photos tab");
                await WaitFor(() => photos.Browser.CoreWebView2.DocumentTitle == "Photos tab fixture", "Shortcut Photos document");
                HashSet<MainWindow> beforeDrive = new HashSet<MainWindow>(window.BrowserTabs);
                await core.ExecuteScriptAsync("window.open('/drive.html','shortcut_drive_" + id + "');void(0);");
                MainWindow drive = await NewTab(window, beforeDrive, "Shortcut Drive tab");
                await WaitFor(() => drive.Browser.CoreWebView2.DocumentTitle == "Drive tab fixture", "Shortcut Drive document");
                await photos.Browser.CoreWebView2.ExecuteScriptAsync("window.shortcutFixtureMarker='photos-kept';");
                await drive.Browser.CoreWebView2.ExecuteScriptAsync("window.shortcutFixtureMarker='drive-kept';");

                bool numbered = Command(window, Keys.Control | Keys.D1) && Object.ReferenceEquals(window.ActiveBrowserTab, window)
                    && Command(window, Keys.Control | Keys.D2) && Object.ReferenceEquals(window.ActiveBrowserTab, photos)
                    && Command(drive, Keys.Control | Keys.D3) && Object.ReferenceEquals(window.ActiveBrowserTab, drive);
                check(numbered, "Ctrl+1, Ctrl+2 and Ctrl+3 select NAS home and application tabs in visible order from the main and child forms");

                Command(window, Keys.Control | Keys.D1);
                check(Command(window, Keys.Control | Keys.D9) && Object.ReferenceEquals(window.ActiveBrowserTab, drive),
                    "Ctrl+9 selects the last open tab even when fewer than nine tabs exist");

                KeyEventArgs homeShortcut = BrowserKey(drive, Keys.Control | Keys.D1);
                await WaitFor(() => Object.ReferenceEquals(window.ActiveBrowserTab, window), "Native Ctrl+1 selection");
                check(homeShortcut.Handled && homeShortcut.SuppressKeyPress,
                    "Ctrl+1 received by a NAS application's native WebView selects NAS home and suppresses the page shortcut");

                KeyEventArgs secondShortcut = BrowserKey(window, Keys.Control | Keys.D2);
                await WaitFor(() => Object.ReferenceEquals(window.ActiveBrowserTab, photos), "Native Ctrl+2 selection");
                KeyEventArgs thirdShortcut = BrowserKey(photos, Keys.Control | Keys.D3);
                await WaitFor(() => Object.ReferenceEquals(window.ActiveBrowserTab, drive), "Native Ctrl+3 selection");
                check(secondShortcut.Handled && secondShortcut.SuppressKeyPress && thirdShortcut.Handled && thirdShortcut.SuppressKeyPress,
                    "Native WebView shortcuts switch between NAS home and live application tabs without requiring header focus");

                bool unavailable = Command(window, Keys.Control | Keys.D8);
                KeyEventArgs missingShortcut = BrowserKey(drive, Keys.Control | Keys.D8);
                await Task.Yield();
                check(unavailable && missingShortcut.Handled && missingShortcut.SuppressKeyPress
                    && Object.ReferenceEquals(window.ActiveBrowserTab, drive) && window.BrowserTabs.Count == originalTabs.Count + 2,
                    "A numbered shortcut for a missing tab is consumed without changing the active tab or creating another tab");

                KeyEventArgs zero = BrowserKey(drive, Keys.Control | Keys.D0);
                KeyEventArgs shift = BrowserKey(drive, Keys.Control | Keys.Shift | Keys.D1);
                KeyEventArgs alt = BrowserKey(drive, Keys.Control | Keys.Alt | Keys.D1);
                await Task.Yield();
                check(!zero.Handled && !zero.SuppressKeyPress && !shift.Handled && !shift.SuppressKeyPress
                    && !alt.Handled && !alt.SuppressKeyPress && Object.ReferenceEquals(window.ActiveBrowserTab, drive),
                    "Ctrl+0 and extra Shift or Alt modifiers retain their existing browser or page behavior");

                bool numpadHome = Command(drive, Keys.Control | Keys.NumPad1) && Object.ReferenceEquals(window.ActiveBrowserTab, window);
                KeyEventArgs numpadSecond = BrowserKey(window, Keys.Control | Keys.NumPad2);
                await WaitFor(() => Object.ReferenceEquals(window.ActiveBrowserTab, photos), "Numpad second-tab selection");
                KeyEventArgs numpadLast = BrowserKey(photos, Keys.Control | Keys.NumPad9);
                await WaitFor(() => Object.ReferenceEquals(window.ActiveBrowserTab, drive), "Numpad last-tab selection");
                check(numpadHome && numpadSecond.Handled && numpadSecond.SuppressKeyPress && numpadLast.Handled && numpadLast.SuppressKeyPress,
                    "Ctrl with number-pad 1, 2 and 9 follows the same home, numbered and last-tab selection rules");

                check(core.Source == source && await core.ExecuteScriptAsync("window.shortcutFixtureMarker") == "\"home-kept\""
                    && await photos.Browser.CoreWebView2.ExecuteScriptAsync("window.shortcutFixtureMarker") == "\"photos-kept\""
                    && await drive.Browser.CoreWebView2.ExecuteScriptAsync("window.shortcutFixtureMarker") == "\"drive-kept\"",
                    "Numbered tab shortcuts preserve every loaded NAS document and its live state");

                window.CloseBrowserTab(photos);
                await WaitFor(() => photos.IsDisposed && !window.BrowserTabs.Contains(photos), "Closing shortcut tab");
                Command(window, Keys.Control | Keys.D1);
                check(Command(window, Keys.Control | Keys.D2) && Object.ReferenceEquals(window.ActiveBrowserTab, drive)
                    && window.BrowserTabs.Count == originalTabs.Count + 1 && core.Source == source,
                    "Closing a tab compacts numbered positions so Ctrl+2 selects the next remaining application");
            }
            finally
            {
                window.BrowserTabPrepared -= prepare;
                foreach (MainWindow tab in window.BrowserTabs.Where(item => !originalTabs.Contains(item)).ToArray())
                    if (!tab.IsDisposed) window.CloseBrowserTab(tab);
                if (!window.IsDisposed) window.SelectBrowserTab(window);
            }
        }

        private static bool Command(Control control, Keys key)
        {
            Message message = Message.Create(control.Handle, 0x0100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
            return (bool)control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { message, key });
        }

        private static KeyEventArgs BrowserKey(MainWindow tab, Keys key)
        {
            KeyEventArgs result = new KeyEventArgs(key);
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(tab.Browser, new object[] { result });
            return result;
        }

        private static async Task<MainWindow> NewTab(MainWindow window, HashSet<MainWindow> before, string description)
        {
            MainWindow result = null;
            await WaitFor(() => {
                result = window.BrowserTabs.FirstOrDefault(tab => !before.Contains(tab) && !tab.IsDisposed && tab.Ready);
                return result != null;
            }, description);
            return result;
        }

        private static async Task WaitFor(Func<bool> condition, string description)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(description);
                await Task.Delay(25);
            }
        }
    }
}
