using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class AddressModalBrowserTests
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr window);

        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string settingsPath, Action<bool, string> check)
        {
            AddressBar address = window.Address;
            ConnectionSettings original = ConnectionSettings.Read(settingsPath);
            string settingsFile = File.ReadAllText(settingsPath);
            string source = core.Source;
            string cookies = await core.ExecuteScriptAsync("document.cookie");
            string storage = await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')");
            string profile = core.Environment.UserDataFolder;
            string historyPath = settingsPath + ".history.json";
            AddressHistoryStore store = new AddressHistoryStore(historyPath);
            ConnectionSettings alternate;
            ConnectionSettings.TryCreate("https://nas-desk-alternate.test/index.html?modal=pick", ConnectionKind.DirectHttps, out alternate);
            store.Add(alternate, "백업 NAS");
            store.Add(original, "메인 NAS");
            string historyFile = File.ReadAllText(historyPath);
            address.SetAddress(original.Url, original.Kind);

            InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
            {
                check(dialog.Modal && Object.ReferenceEquals(dialog.Owner, window) && !IsWindowEnabled(window.Handle)
                    && !address.IsEditing && address.Input.ReadOnly && address.AddressText == original.Url,
                    "Clicking the visible locked address opens an owned history modal without unlocking or changing its address"
                    + " [modal=" + dialog.Modal + "; ownerSame=" + Object.ReferenceEquals(dialog.Owner, window)
                    + "; nativeOwnerEnabled=" + IsWindowEnabled(window.Handle) + "; editing=" + address.IsEditing
                    + "; readOnly=" + address.Input.ReadOnly + "]");
                window.OpenAddressEditor();
                typeof(MainWindow).GetMethod("ShowAddressHistory", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[0]);
                check(Object.ReferenceEquals(window.HistoryDialog, dialog) && !address.IsEditing
                    && Application.OpenForms.Cast<Form>().OfType<AddressHistoryDialog>().Count() == 1,
                    "A history modal rejects duplicate opening and direct editor activation while it is displayed");
            });
            check(window.HistoryDialog == null && !address.IsEditing && address.Input.Visible && address.TypePicker.Visible
                && address.Input.ReadOnly && File.ReadAllText(settingsPath) == settingsFile
                && File.ReadAllText(historyPath) == historyFile && core.Source == source,
                "Canceling address history retains the visible locked address, saved settings, history and current document");

            InModal(window, delegate { Click(address.Input.Parent); }, delegate(AddressHistoryDialog dialog)
            {
                check(!address.IsEditing && dialog.Modal,
                    "The visible address frame also opens the same history modal without entering edit mode");
            });
            address.Input.Focus();
            InModal(window, delegate { Command(address, Keys.Enter); }, delegate(AddressHistoryDialog dialog)
            {
                check(dialog.Modal && !address.IsEditing,
                    "Enter on the focused read-only address opens history for keyboard users");
            });

            window.OpenAddressEditor();
            const string draft = "https://unsaved-modal.test/";
            address.AddressText = draft;
            address.Kind = ConnectionKind.SynologyQuickConnect;
            Click(address.Input);
            check(window.HistoryDialog == null && address.IsEditing && !address.Input.ReadOnly && address.AddressText == draft,
                "Explicit address editing allows ordinary caret clicks instead of reopening history");
            InModal(window, delegate { address.HistoryButton.PerformClick(); }, delegate(AddressHistoryDialog dialog)
            {
                check(dialog.Modal && address.IsEditing && address.AddressText == draft,
                    "The history action can open a modal while preserving an existing address draft");
            });
            check(address.IsEditing && address.AddressText == draft && address.Kind == ConnectionKind.SynologyQuickConnect
                && File.ReadAllText(settingsPath) == settingsFile && File.ReadAllText(historyPath) == historyFile && core.Source == source,
                "Canceling history during editing preserves both draft fields without saving or navigating");
            Command(address, Keys.Escape);

            InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
            {
                TextBox name = Field<TextBox>(dialog, "selectedNameInput");
                name.Text = "집 NAS";
                Field<ShellButton>(dialog, "renameButton").PerformClick();
            });
            check(store.ReadEntries().First(item => item.Url == original.Url && item.Kind == original.Kind).Name == "집 NAS"
                && File.ReadAllText(settingsPath) == settingsFile && core.Source == source
                && await core.ExecuteScriptAsync("document.cookie") == cookies
                && await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')") == storage,
                "Renaming through the address modal changes only history and retains the NAS connection and login storage");
            string renamedHistory = File.ReadAllText(historyPath);

            InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
            {
                ListBox list = Field<ListBox>(dialog, "historyList");
                int index = Enumerable.Range(0, list.Items.Count).First(item => list.Items[item].ToString().Contains(alternate.Url));
                list.SelectedIndex = index;
                Field<ShellButton>(dialog, "useButton").PerformClick();
            });
            check(window.HistoryDialog == null && address.IsEditing && address.Input.Visible && !address.Input.ReadOnly
                && address.ConnectButton.Visible && address.AddressText == alternate.Url && address.Kind == alternate.Kind
                && File.ReadAllText(settingsPath) == settingsFile && File.ReadAllText(historyPath) == renamedHistory
                && Object.ReferenceEquals(core, window.Browser.CoreWebView2) && core.Source == source
                && core.Environment.UserDataFolder == profile,
                "Using a modal history row fills an editable address and still requires an explicit connection");
            await SubmitAndWait(core, alternate.Url, delegate { address.ConnectButton.PerformClick(); });
            check(!address.IsEditing && address.Input.Visible && address.Input.ReadOnly && address.TypePicker.Visible
                && !address.ConnectButton.Visible && core.Source == alternate.Url
                && ConnectionSettings.Read(settingsPath).Url == alternate.Url && store.ReadEntries()[0].Name == "백업 NAS",
                "Connecting a selected history address keeps the URL visible and preserves its NAS name");

            window.OpenAddressEditor();
            address.Kind = original.Kind;
            address.AddressText = original.Url;
            await SubmitAndWait(core, original.Url, delegate { address.ConnectButton.PerformClick(); });
            check(address.Input.Visible && address.Input.ReadOnly && !address.IsEditing
                && ConnectionSettings.Read(settingsPath).Url == original.Url && core.Source == original.Url,
                "Restoring the original NAS keeps the saved address visible after connection");
        }

        private static void InModal(MainWindow window, Action trigger, Action<AddressHistoryDialog> inspect)
        {
            Exception failure = null;
            bool observed = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            using (Timer timer = new Timer { Interval = 20 })
            {
                timer.Tick += delegate
                {
                    AddressHistoryDialog dialog = window.HistoryDialog;
                    if (dialog == null || !dialog.Visible)
                    {
                        if (DateTime.UtcNow < deadline) return;
                        timer.Stop();
                        failure = new TimeoutException("Address history modal did not become visible.");
                        foreach (AddressHistoryDialog open in Application.OpenForms.Cast<Form>().OfType<AddressHistoryDialog>().ToArray())
                            open.Close();
                        return;
                    }
                    timer.Stop();
                    observed = true;
                    try { inspect(dialog); }
                    catch (Exception error) { failure = error; }
                    finally
                    {
                        if (!dialog.IsDisposed && dialog.DialogResult == DialogResult.None)
                        {
                            dialog.DialogResult = DialogResult.Cancel;
                            dialog.Close();
                        }
                    }
                };
                timer.Start();
                trigger();
                timer.Stop();
            }
            if (failure != null) throw failure;
            if (!observed) throw new Exception("The address action did not open a modal history dialog.");
        }

        private static T Field<T>(AddressHistoryDialog dialog, string field) where T : class
        {
            return typeof(AddressHistoryDialog).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog) as T;
        }

        private static void Click(Control control)
        {
            // Dispatch the control's normal mouse event without moving the
            // user's desktop pointer. Native WM_MOUSEUP hit testing depends on
            // the physical pointer position and is unsuitable for this fixture.
            string mouseEvent = control is TextBox ? "OnMouseClick" : "OnMouseDown";
            typeof(Control).GetMethod(mouseEvent, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0) });
        }

        private static bool Command(Control control, Keys key)
        {
            Message message = Message.Create(control.Handle, 0x0100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
            return (bool)control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { message, key });
        }

        private static async Task SubmitAndWait(CoreWebView2 core, string target, Action submit)
        {
            bool completed = false;
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.IsSuccess && core.Source == target) completed = true;
            };
            core.NavigationCompleted += handler;
            try
            {
                submit();
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (!completed)
                {
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("History address did not connect.");
                    await Task.Delay(50);
                }
            }
            finally { core.NavigationCompleted -= handler; }
        }
    }
}
