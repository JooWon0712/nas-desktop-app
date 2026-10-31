using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class AddressReadOnlyTests
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr word, IntPtr data);

        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string settingsPath, Action<bool, string> check)
        {
            AddressBar address = window.Address;
            ConnectionSettings original = ConnectionSettings.Read(settingsPath);
            string settingsFile = File.ReadAllText(settingsPath);
            string source = core.Source;
            string profile = core.Environment.UserDataFolder;
            AddressHistoryStore history = new AddressHistoryStore(settingsPath + ".history.json");
            string historyFile = Snapshot(history);
            string testFolder = Path.GetFullPath(Path.GetDirectoryName(settingsPath)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            check(Path.GetFullPath(profile).StartsWith(testFolder, StringComparison.OrdinalIgnoreCase),
                "Read-only address tests use only their isolated browser profile and local settings");
            check(IsReadOnly(address, original) && !address.Input.CanUndo,
                "The top address and connection type remain visible and read-only with history available");

            address.Input.Focus();
            check(address.Input.Focused && window.HistoryDialog == null && IsReadOnly(address, original)
                && File.ReadAllText(settingsPath) == settingsFile && core.Source == source,
                "Focusing the displayed address alone neither edits it nor opens or changes a connection");
            address.Input.SelectAll();
            SendMessage(address.Input.Handle, 0x0102, (IntPtr)'x', IntPtr.Zero); // WM_CHAR
            SendMessage(address.Input.Handle, 0x0102, (IntPtr)'\b', IntPtr.Zero);
            SendMessage(address.Input.Handle, 0x0303, IntPtr.Zero, IntPtr.Zero); // WM_CLEAR; no clipboard access.
            check(IsReadOnly(address, original) && File.ReadAllText(settingsPath) == settingsFile
                && core.Source == source && Snapshot(history) == historyFile && window.HistoryDialog == null,
                "Native typing, backspace and clear cannot alter or submit the read-only top address");

            address.Input.Text = "https://display-only-fixture.test/";
            address.TypePicker.Kind = ConnectionKind.SynologyQuickConnect;
            check(File.ReadAllText(settingsPath) == settingsFile && core.Source == source
                && Snapshot(history) == historyFile && Object.ReferenceEquals(core, window.Browser.CoreWebView2),
                "Programmatic display changes do not save settings, update history or navigate the browser");
            address.SetAddress(original.Url, original.Kind);
            address.TypePicker.PerformClick();
            check(IsReadOnly(address, original) && window.HistoryDialog == null
                && File.ReadAllText(settingsPath) == settingsFile && core.Source == source,
                "The disabled connection type cannot change the saved NAS or create a direct editor");

            using (Form owner = new Form { Opacity = 0, ShowInTaskbar = false, ClientSize = new Size(760, 100) })
            using (AddressBar bar = new AddressBar { Dock = DockStyle.Top })
            {
                owner.Controls.Add(bar);
                bar.SetAddress(original.Url, original.Kind);
                int requests = 0;
                bar.HistoryRequested += delegate { requests++; };
                owner.Show();
                bar.Input.Focus();
                bool enter = Command(bar, Keys.Enter);
                bool shortcut = Command(bar, Keys.Control | Keys.L);
                bool alternateDown = Command(bar, Keys.Alt | Keys.Down);
                check(enter && shortcut && alternateDown && requests == 3 && IsReadOnly(bar, original),
                    "Enter, Ctrl+L and Alt+Down request history while leaving the top address read-only");
                Click(bar.Input);
                bar.HistoryButton.PerformClick();
                check(requests == 5 && IsReadOnly(bar, original),
                    "Address clicks and the history action request selection without enabling direct editing");
                bar.HistoryEnabled = false;
                bar.Input.Focus();
                Click(bar.Input);
                bar.HistoryButton.PerformClick();
                Command(bar, Keys.Enter);
                Command(bar, Keys.Control | Keys.L);
                Command(bar, Keys.Alt | Keys.Down);
                check(requests == 5 && bar.Input.Visible && bar.Input.ReadOnly && bar.TypePicker.Visible
                    && !bar.TypePicker.Enabled && !bar.HistoryButton.Enabled && bar.AddressText == original.Url,
                    "Disabling history preserves the visible address and blocks mouse, button and keyboard selection");
                owner.Close();
            }

            string cookies = await core.ExecuteScriptAsync("document.cookie");
            string storage = await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')");
            AddressHistoryEntry current = history.ReadEntries().FirstOrDefault(item => item.Url == original.Url && item.Kind == original.Kind);
            check(current != null && history.Remove(original)
                && !new AddressHistoryStore(settingsPath + ".history.json").Read().Any(item => item.Url == original.Url && item.Kind == original.Kind)
                && File.ReadAllText(settingsPath) == settingsFile && IsReadOnly(address, original)
                && Object.ReferenceEquals(core, window.Browser.CoreWebView2) && core.Source == source
                && core.Environment.UserDataFolder == profile && await core.ExecuteScriptAsync("document.cookie") == cookies
                && await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')") == storage,
                "Deleting the displayed connection from history keeps its address, settings, document and login storage");
            if (current != null) history.Add(original, current.Name);

            HashSet<MainWindow> existing = new HashSet<MainWindow>(window.BrowserTabs);
            int topLevelWindows = Application.OpenForms.Cast<Form>().Count(form => form.TopLevel);
            MainWindow popup = null;
            try
            {
                await core.ExecuteScriptAsync("window.open('about:blank','readonly_address_popup_fixture');");
                await WaitFor(() => {
                    popup = window.BrowserTabs.FirstOrDefault(tab => !existing.Contains(tab));
                    return popup != null && popup.Ready && popup.Parent != null;
                }, "Read-only NAS application tab");
                string popupAddress = popup.Address.AddressText;
                Click(popup.Address.Input);
                popup.Address.HistoryButton.PerformClick();
                popup.ShowAddressHistory();
                Command(popup, Keys.Control | Keys.L);
                KeyEventArgs shortcut = new KeyEventArgs(Keys.Control | Keys.L);
                typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(popup.Browser, new object[] { shortcut });
                bool accepted = popup.ApplyConnection(original);
                await Task.Yield();
                check(!accepted && !popup.Address.HistoryEnabled && popup.HistoryDialog == null
                    && !popup.TopLevel && popup.Parent != null && window.Visible
                    && Application.OpenForms.Cast<Form>().Count(form => form.TopLevel) == topLevelWindows
                    && Object.ReferenceEquals(window.ActiveBrowserTab, popup)
                    && !popup.Address.Input.Visible && popup.Address.Input.ReadOnly && !popup.Address.TypePicker.Visible
                    && !popup.Address.TypePicker.Enabled && !popup.Address.HistoryButton.Enabled
                    && popup.Address.AddressText == popupAddress && File.ReadAllText(settingsPath) == settingsFile
                    && IsReadOnly(address, original) && core.Source == source,
                    "NAS application tabs hide duplicate address controls and cannot replace the main connection or its visible read-only address");
            }
            finally
            {
                if (popup != null && !popup.IsDisposed) { popup.RequestExit(); popup.Dispose(); }
            }
        }

        private static bool IsReadOnly(AddressBar bar, ConnectionSettings expected)
        {
            return bar.Input.Visible && bar.Input.ReadOnly && bar.TypePicker.Visible && !bar.TypePicker.Enabled
                && bar.HistoryEnabled && bar.HistoryButton.Visible && bar.HistoryButton.Enabled
                && bar.AddressText == expected.Url && bar.Kind == expected.Kind;
        }

        private static string Snapshot(AddressHistoryStore history)
        {
            return String.Join("\n", history.ReadEntries().Select(item => item.Type + "\t" + item.Url + "\t" + item.Name));
        }

        private static bool Command(Control control, Keys key)
        {
            Message message = Message.Create(control.Handle, 0x0100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
            return (bool)control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { message, key });
        }

        private static void Click(Control control)
        {
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 8, 8, 0) });
        }

        private static async Task WaitFor(Func<bool> condition, string description)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(description);
                await Task.Delay(50);
            }
        }
    }
}
