using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class AddressAutoEditTests
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr word, IntPtr data);

        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string settingsPath, Action<bool, string> check)
        {
            AddressBar address = window.Address;
            ConnectionSettings original = ConnectionSettings.Read(settingsPath);
            string originalFile = File.ReadAllText(settingsPath);
            string originalPage = core.Source;

            address.SetAddress(original.Url, original.Kind);
            address.Input.Focus();
            check(!address.IsEditing && address.Input.ReadOnly && !address.Input.CanUndo
                && !address.LockButton.Editing && !address.ConnectButton.Enabled,
                "Setting or focusing a saved address keeps the edit lock and clears old undo history");

            SendMouseClick(address.Input);
            check(address.IsEditing && !address.Input.ReadOnly && address.LockButton.Editing
                && address.ConnectButton.Enabled && address.Input.SelectionLength == address.Input.TextLength,
                "Clicking a locked address automatically opens editing and selects the saved address");
            address.AddressText = "https://cancel-auto-edit.test/";
            address.Kind = ConnectionKind.SynologyQuickConnect;
            check(Command(address, Keys.Escape) && IsLocked(address, original)
                && File.ReadAllText(settingsPath) == originalFile && core.Source == originalPage,
                "Escape restores both draft address and connection type without saving or navigating");

            address.LockButton.PerformClick();
            address.AddressText = "https://cancel-lock-button.test/";
            address.Kind = ConnectionKind.SynologyQuickConnect;
            address.LockButton.PerformClick();
            check(IsLocked(address, original) && File.ReadAllText(settingsPath) == originalFile
                && !address.Input.CanUndo && core.Source == originalPage,
                "The open lock button cancels changes and returns to the committed address");

            address.Input.Focus();
            KeyDown(address.Input, Keys.Control | Keys.C);
            KeyDown(address.Input, Keys.Control | Keys.A);
            KeyDown(address.Input, Keys.Home);
            KeyDown(address.Input, Keys.Left);
            check(IsLocked(address, original),
                "Copy, select-all and navigation key intents do not unlock a saved address");

            Character(address.Input, 'h');
            check(address.IsEditing && address.AddressText == "h",
                "The first character typed into a focused locked address replaces it without losing that character");
            const string enterUrl = "https://nas-desk-alternate.test/index.html?auto-edit=enter";
            address.AddressText = enterUrl;
            address.Kind = ConnectionKind.DirectHttps;
            check(Command(address, Keys.Enter), "Enter submits an automatically opened address editor");
            await WaitFor(() => core.Source == enterUrl && core.DocumentTitle == "NAS Desk local fixture", "Automatic edit Enter navigation");
            ConnectionSettings entered = ConnectionSettings.Read(settingsPath);
            check(entered.Url == enterUrl && entered.Kind == ConnectionKind.DirectHttps && IsLocked(address, entered),
                "A valid Enter submission saves the address and closes the edit lock");

            address.Input.Focus();
            check(!address.IsEditing, "Focusing again after a valid submission leaves the address locked");
            Character(address.Input, 'z');
            check(address.IsEditing && address.AddressText == "z",
                "Typing immediately after saving a focused address opens a fresh draft and keeps its first character");
            Command(address, Keys.Escape);

            string committedFile = File.ReadAllText(settingsPath);
            KeyDown(address.Input, Keys.Control | Keys.V);
            bool editableBeforePasteFixture = address.IsEditing && !address.Input.ReadOnly;
            // Do not send WM_PASTE or touch the system clipboard. This verifies
            // edit intent ordering, then supplies a controlled replacement text.
            address.Input.SelectedText = "https://paste-fixture.test/";
            check(editableBeforePasteFixture && address.AddressText == "https://paste-fixture.test/"
                && File.ReadAllText(settingsPath) == committedFile,
                "Ctrl+V key intent unlocks before controlled replacement text without reading the clipboard");
            Command(address, Keys.Escape);
            KeyDown(address.Input, Keys.Shift | Keys.Insert);
            bool editableBeforeShiftInsertFixture = address.IsEditing && !address.Input.ReadOnly;
            address.Input.SelectedText = "https://shift-insert-fixture.test/";
            check(editableBeforeShiftInsertFixture && address.AddressText == "https://shift-insert-fixture.test/"
                && File.ReadAllText(settingsPath) == committedFile,
                "Shift+Insert key intent unlocks before controlled replacement text without reading the clipboard");
            Command(address, Keys.Escape);

            address.Input.Focus();
            Character(address.Input, 'h');
            const string invalid = "http://invalid-auto-edit.test/";
            address.AddressText = invalid;
            check(Command(address, Keys.Enter) && address.IsEditing && !address.Input.ReadOnly
                && address.AddressText == invalid && File.ReadAllText(settingsPath) == committedFile
                && core.Source == enterUrl,
                "An invalid Enter submission keeps its draft open and preserves the saved address and document");
            Command(address, Keys.Escape);

            address.TypePicker.Focus();
            EventHandler typeEdit = (EventHandler)typeof(ConnectionTypePicker).GetField("EditRequested", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(address.TypePicker);
            typeEdit(address.TypePicker, EventArgs.Empty);
            check(address.IsEditing && address.TypePicker.Focused && !address.Input.Focused,
                "Connection-type edit intent unlocks without stealing focus from the type picker");
            address.Kind = ConnectionKind.SynologyQuickConnect;
            check(Command(address, Keys.Escape) && IsLocked(address, entered),
                "Cancelling connection-type editing restores the saved HTTPS type and lock");

            HashSet<Form> existing = new HashSet<Form>(Application.OpenForms.Cast<Form>());
            MainWindow popup = null;
            try
            {
                await core.ExecuteScriptAsync("window.open('about:blank','auto_edit_popup_fixture');");
                await WaitFor(() => {
                    popup = Application.OpenForms.Cast<Form>().OfType<MainWindow>().FirstOrDefault(form => !existing.Contains(form));
                    return popup != null && popup.Ready;
                }, "Read-only popup address editor");
                string popupAddress = popup.Address.AddressText;
                SendMouseClick(popup.Address.Input);
                Character(popup.Address.Input, 'p');
                KeyDown(popup.Address.Input, Keys.Control | Keys.V);
                popup.Address.LockButton.PerformClick();
                popup.Address.IsEditing = true;
                check(!popup.Address.EditingEnabled && !popup.Address.IsEditing && popup.Address.Input.ReadOnly
                    && !popup.Address.TypePicker.Enabled && !popup.Address.LockButton.Enabled
                    && !popup.Address.ConnectButton.Enabled && popup.Address.AddressText == popupAddress,
                    "Popup address fields remain read-only for mouse, typing, paste intent and lock actions");
            }
            finally
            {
                if (popup != null && !popup.IsDisposed) { popup.RequestExit(); popup.Dispose(); }
            }

            address.IsEditing = true;
            address.Kind = original.Kind;
            address.AddressText = original.Url;
            address.ConnectButton.PerformClick();
            await WaitFor(() => core.Source == original.Url && core.DocumentTitle == "NAS Desk local fixture", "Restore the original address fixture");
            check(IsLocked(address, original) && ConnectionSettings.Read(settingsPath).Url == original.Url
                && ConnectionSettings.Read(settingsPath).Kind == original.Kind,
                "The connection button saves and locks the editor while restoring the original test connection");
        }

        private static bool IsLocked(AddressBar address, ConnectionSettings expected)
        {
            return !address.IsEditing && address.Input.ReadOnly && !address.LockButton.Editing
                && !address.ConnectButton.Enabled && address.AddressText == expected.Url && address.Kind == expected.Kind;
        }

        private static bool Command(AddressBar address, Keys key)
        {
            Message message = Message.Create(address.Input.Handle, 0x0100, (IntPtr)key, IntPtr.Zero);
            return address.PreProcessMessage(ref message);
        }

        private static void Character(AddressInput input, char value)
        {
            SendMessage(input.Handle, 0x0102, (IntPtr)value, IntPtr.Zero);
        }

        private static void KeyDown(AddressInput input, Keys key)
        {
            typeof(AddressInput).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(input, new object[] { new KeyEventArgs(key) });
        }

        private static void SendMouseClick(AddressInput input)
        {
            IntPtr point = (IntPtr)((5 << 16) | 5);
            SendMessage(input.Handle, 0x0201, (IntPtr)1, point);
            SendMessage(input.Handle, 0x0202, IntPtr.Zero, point);
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
