using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class AddressAutoEditTests
    {
        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string settingsPath, Action<bool, string> check)
        {
            AddressBar address = window.Address;
            ConnectionSettings original = ConnectionSettings.Read(settingsPath);
            string originalFile = File.ReadAllText(settingsPath);
            string originalPage = core.Source;
            AddressHistoryStore history = new AddressHistoryStore(settingsPath + ".history.json");
            string originalHistory = Snapshot(history);

            address.SetAddress(original.Url, original.Kind);
            check(IsLocked(address, original) && !address.Input.CanUndo,
                "A saved address keeps its URL and type visible and read-only while clearing old undo history");

            address.LockButton.PerformClick();
            check(address.IsEditing && address.Input.Visible && !address.Input.ReadOnly && address.Input.Focused
                && address.TypePicker.Visible && address.TypePicker.Enabled && address.ConnectButton.Visible
                && address.ConnectButton.Enabled && address.Input.SelectionLength == address.Input.TextLength
                && File.ReadAllText(settingsPath) == originalFile && core.Source == originalPage
                && Snapshot(history) == originalHistory,
                "The address change button opens and selects the saved address without saving or navigating");
            address.AddressText = "https://cancel-address-editor.test/";
            address.Kind = ConnectionKind.SynologyQuickConnect;
            check(Command(address, Keys.Escape) && IsLocked(address, original)
                && File.ReadAllText(settingsPath) == originalFile && core.Source == originalPage
                && Snapshot(history) == originalHistory,
                "Escape restores the visible committed address and type without changing history");

            address.LockButton.PerformClick();
            address.AddressText = "https://cancel-button-fixture.test/";
            address.Kind = ConnectionKind.SynologyQuickConnect;
            address.LockButton.PerformClick();
            check(IsLocked(address, original) && File.ReadAllText(settingsPath) == originalFile
                && !address.Input.CanUndo && core.Source == originalPage,
                "The cancel button discards both draft fields and clears undo while keeping the saved address visible");

            check(Command(window, Keys.Control | Keys.L) && address.IsEditing && address.Input.Visible
                && address.Input.Focused && address.Input.SelectionLength == address.Input.TextLength,
                "Ctrl+L explicitly enables and focuses the visible address editor");
            const string keyboardDraft = "https://unsaved-keyboard-fixture.test/";
            address.AddressText = keyboardDraft;
            address.Kind = ConnectionKind.SynologyQuickConnect;
            check(Command(window, Keys.Control | Keys.L) && address.AddressText == keyboardDraft
                && address.Kind == ConnectionKind.SynologyQuickConnect && address.Input.Focused
                && File.ReadAllText(settingsPath) == originalFile && Snapshot(history) == originalHistory
                && core.Source == originalPage,
                "Ctrl+L preserves an existing unsaved address and type while selecting the draft");
            Command(address, Keys.Escape);

            window.Browser.Focus();
            KeyEventArgs browserShortcut = BrowserCommand(window, Keys.Control | Keys.L);
            await WaitFor(() => address.IsEditing && address.Input.Visible && address.Input.Focused,
                "Browser Ctrl+L opens the address editor");
            check(browserShortcut.Handled && browserShortcut.SuppressKeyPress
                && File.ReadAllText(settingsPath) == originalFile && core.Source == originalPage
                && Snapshot(history) == originalHistory,
                "The browser key route handles Ctrl+L and opens the editor without saving or navigating");
            Command(address, Keys.Escape);

            ConnectionSettings recalled;
            ConnectionSettings.TryCreate("ui-history-fixture", ConnectionKind.SynologyQuickConnect, out recalled);
            history.Add(recalled);
            string recalledHistory = Snapshot(history);
            check(window.UseHistoryAddress(recalled) && address.IsEditing && address.Input.Visible
                && address.AddressText == recalled.Url && address.Kind == recalled.Kind
                && File.ReadAllText(settingsPath) == originalFile && core.Source == originalPage
                && Snapshot(history) == recalledHistory,
                "Choosing a stored address fills only an editable draft and requires an explicit connection");
            check(Command(address, Keys.Escape) && IsLocked(address, original)
                && File.ReadAllText(settingsPath) == originalFile && core.Source == originalPage,
                "A selected history draft can be cancelled without replacing the current connection");
            check(!window.UseHistoryAddress(new ConnectionSettings { Url = "http://invalid-history-fixture.test/", Type = "https" })
                && IsLocked(address, original) && File.ReadAllText(settingsPath) == originalFile
                && Snapshot(history) == recalledHistory && core.Source == originalPage,
                "Invalid history candidates cannot open a draft, save settings or navigate");

            const string enterUrl = "https://nas-desk-alternate.test/index.html?address-editor=enter";
            ConnectionSettings namedEnter;
            ConnectionSettings.TryCreate(enterUrl, ConnectionKind.DirectHttps, out namedEnter);
            history.Add(namedEnter, "Enter fixture NAS");
            window.OpenAddressEditor();
            address.AddressText = enterUrl;
            address.Kind = ConnectionKind.DirectHttps;
            bool enterHandled = false;
            await SubmitAndWait(core, enterUrl, delegate { enterHandled = Command(address, Keys.Enter); }, "Address editor Enter navigation");
            ConnectionSettings entered = ConnectionSettings.Read(settingsPath);
            check(enterHandled && entered.Url == enterUrl && entered.Kind == ConnectionKind.DirectHttps
                && IsLocked(address, entered) && !address.Input.CanUndo
                && history.ReadEntries().Any(item => item.Url == enterUrl && item.Kind == ConnectionKind.DirectHttps
                    && item.Name == "Enter fixture NAS"),
                "A valid Enter submission saves the visible locked address and preserves its stored NAS name");

            string committedFile = File.ReadAllText(settingsPath);
            string committedHistory = Snapshot(history);
            window.OpenAddressEditor();
            const string invalid = "http://invalid-address-editor.test/";
            address.AddressText = invalid;
            check(Command(address, Keys.Enter) && address.IsEditing && address.Input.Visible && !address.Input.ReadOnly
                && address.AddressText == invalid && File.ReadAllText(settingsPath) == committedFile
                && Snapshot(history) == committedHistory && core.Source == enterUrl,
                "An invalid Enter submission preserves its visible draft, saved address, history and NAS document");
            Command(address, Keys.Escape);

            window.OpenAddressEditor();
            address.TypePicker.Focus();
            address.Kind = ConnectionKind.SynologyQuickConnect;
            check(address.TypePicker.Focused && !address.Input.Focused && Command(address, Keys.Escape)
                && IsLocked(address, entered),
                "Connection type editing keeps picker focus and Escape restores the committed HTTPS type");

            string cookies = await core.ExecuteScriptAsync("document.cookie");
            string storage = await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')");
            string profile = core.Environment.UserDataFolder;
            check(history.Remove(entered) && !new AddressHistoryStore(settingsPath + ".history.json").Read()
                .Any(item => item.Url == entered.Url && item.Kind == entered.Kind)
                && File.ReadAllText(settingsPath) == committedFile && IsLocked(address, entered)
                && core.Source == enterUrl && Object.ReferenceEquals(core, window.Browser.CoreWebView2)
                && core.Environment.UserDataFolder == profile && await core.ExecuteScriptAsync("document.cookie") == cookies
                && await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')") == storage,
                "Deleting the current address from history persists only that deletion and keeps settings, page and session storage");

            HashSet<Form> existing = new HashSet<Form>(Application.OpenForms.Cast<Form>());
            MainWindow popup = null;
            try
            {
                await core.ExecuteScriptAsync("window.open('about:blank','address_editor_popup_fixture');");
                await WaitFor(() => {
                    popup = Application.OpenForms.Cast<Form>().OfType<MainWindow>().FirstOrDefault(form => !existing.Contains(form));
                    return popup != null && popup.Ready;
                }, "Read-only popup address editor");
                string popupAddress = popup.Address.AddressText;
                popup.Address.LockButton.PerformClick();
                popup.Address.HistoryButton.PerformClick();
                popup.Address.IsEditing = true;
                popup.OpenAddressEditor();
                Command(popup, Keys.Control | Keys.L);
                BrowserCommand(popup, Keys.Control | Keys.L);
                bool accepted = popup.UseHistoryAddress(recalled);
                check(!accepted && !popup.Address.EditingEnabled && !popup.Address.IsEditing
                    && popup.Address.Input.ReadOnly && popup.Address.Input.Visible && popup.Address.TypePicker.Visible
                    && !popup.Address.TypePicker.Enabled && !popup.Address.LockButton.Visible && !popup.Address.LockButton.Enabled
                    && !popup.Address.HistoryButton.Visible && !popup.Address.HistoryButton.Enabled
                    && !popup.Address.ConnectButton.Visible && !popup.Address.ConnectButton.Enabled
                    && popup.Address.AddressText == popupAddress && File.ReadAllText(settingsPath) == committedFile
                    && core.Source == enterUrl,
                    "NAS popups display a read-only address but cannot edit or select history through buttons or Ctrl+L");
            }
            finally
            {
                if (popup != null && !popup.IsDisposed) { popup.RequestExit(); popup.Dispose(); }
            }

            window.OpenAddressEditor();
            address.Kind = original.Kind;
            address.AddressText = original.Url;
            await SubmitAndWait(core, original.Url, delegate { address.ConnectButton.PerformClick(); }, "Restore the original address fixture");
            check(IsLocked(address, original) && ConnectionSettings.Read(settingsPath).Url == original.Url
                && ConnectionSettings.Read(settingsPath).Kind == original.Kind
                && history.Read().Any(item => item.Url == original.Url && item.Kind == original.Kind),
                "The connection button saves and records the original test connection and leaves its locked address visible");
        }

        private static bool IsLocked(AddressBar address, ConnectionSettings expected)
        {
            return !address.IsEditing && address.Input.ReadOnly && address.Input.Visible && address.TypePicker.Visible
                && !address.TypePicker.Enabled && !address.ConnectButton.Visible && !address.ConnectButton.Enabled
                && !address.LockButton.Editing && address.LockButton.Visible && address.LockButton.Enabled
                && address.HistoryButton.Visible && address.HistoryButton.Enabled
                && address.AddressText == expected.Url && address.Kind == expected.Kind;
        }

        private static string Snapshot(AddressHistoryStore history)
        {
            return String.Join("\n", history.ReadEntries().Select(item => item.Type + "\t" + item.Url + "\t" + item.Name));
        }

        private static bool Command(Control control, Keys key)
        {
            // Exercise the form/control command route without changing global
            // keyboard state, synthesizing hidden input events or reading a clipboard.
            Message message = Message.Create(control.Handle, 0x0100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
            return (bool)control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { message, key });
        }

        private static KeyEventArgs BrowserCommand(MainWindow window, Keys key)
        {
            KeyEventArgs args = new KeyEventArgs(key);
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window.Browser, new object[] { args });
            return args;
        }

        private static async Task SubmitAndWait(CoreWebView2 core, string target, Action submit, string description)
        {
            bool loaded = false;
            EventHandler<CoreWebView2NavigationCompletedEventArgs> completed = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.IsSuccess && core.Source == target) loaded = true;
            };
            core.NavigationCompleted += completed;
            try
            {
                submit();
                await WaitFor(() => loaded && core.Source == target && core.DocumentTitle == "NAS Desk local fixture", description);
            }
            finally { core.NavigationCompleted -= completed; }
        }

        private static async Task WaitFor(Func<bool> ready, string description)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!ready())
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException(description);
                await Task.Delay(50);
            }
        }
    }
}
