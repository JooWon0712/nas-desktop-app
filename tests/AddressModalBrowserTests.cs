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

        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string settingsPath, string fixture, Action<bool, string> check)
        {
            AddressBar address = window.Address;
            ConnectionSettings original = ConnectionSettings.Read(settingsPath);
            string settingsFile = File.ReadAllText(settingsPath);
            string source = core.Source;
            string cookies = await core.ExecuteScriptAsync("document.cookie");
            string storage = await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')");
            string profile = core.Environment.UserDataFolder;
            string[] fixtureHosts = { "nas-desk.test", "nas-desk-alternate.test" };
            string historyPath = settingsPath + ".history.json";
            AddressHistoryStore store = new AddressHistoryStore(historyPath);
            ConnectionSettings alternate;
            ConnectionSettings.TryCreate("https://nas-desk-alternate.test/index.html?modal=pick", ConnectionKind.DirectHttps, out alternate);
            store.Add(alternate, "백업 NAS");
            store.Add(original, "메인 NAS");
            string historyFile = File.ReadAllText(historyPath);

            InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
            {
                check(dialog.Modal && Object.ReferenceEquals(dialog.Owner, window) && !IsWindowEnabled(window.Handle)
                    && address.Input.ReadOnly && !address.TypePicker.Enabled && address.AddressText == original.Url,
                    "Clicking the permanently read-only address opens an owned modal and blocks its owner input");
                window.ShowAddressHistory();
                check(Object.ReferenceEquals(window.HistoryDialog, dialog)
                    && Application.OpenForms.Cast<Form>().OfType<AddressHistoryDialog>().Count() == 1,
                    "Repeated history requests cannot open duplicate connection modals");
            });
            check(window.HistoryDialog == null && address.Input.Visible && address.Input.ReadOnly
                && File.ReadAllText(settingsPath) == settingsFile && File.ReadAllText(historyPath) == historyFile && core.Source == source,
                "Canceling the connection modal preserves the displayed address, settings, history and current NAS");

            InModal(window, delegate { Click(address.Input.Parent); }, delegate(AddressHistoryDialog dialog)
            {
                check(dialog.Modal && address.Input.ReadOnly, "The address frame opens the same modal without enabling URL editing");
            });
            InModal(window, delegate { Command(window, Keys.Control | Keys.L); }, delegate(AddressHistoryDialog dialog)
            {
                check(dialog.Modal && address.Input.ReadOnly, "Ctrl+L opens connection history while the main URL remains read-only");
            });
            window.Browser.Focus();
            KeyEventArgs browserCommand = new KeyEventArgs(Keys.Control | Keys.L);
            InModal(window, delegate
            {
                typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window.Browser, new object[] { browserCommand });
                Application.DoEvents();
            }, delegate(AddressHistoryDialog dialog)
            {
                check(dialog.Modal && browserCommand.Handled && browserCommand.SuppressKeyPress && address.Input.ReadOnly,
                    "The native browser keyboard route opens the modal instead of an outside URL editor");
            });

            InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
            {
                Field<TextBox>(dialog, "addressInput").Text = "https://unsaved-modal.test/";
                Field<TextBox>(dialog, "addNameInput").Text = "미저장 NAS";
                check(address.AddressText == original.Url && address.Input.ReadOnly && core.Source == source,
                    "Unsubmitted URL and name input inside the modal cannot change the main address or connection");
            });
            check(File.ReadAllText(settingsPath) == settingsFile && File.ReadAllText(historyPath) == historyFile,
                "Canceling unsubmitted modal input does not save a URL or history entry");

            InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
            {
                Field<TextBox>(dialog, "selectedNameInput").Text = "집 NAS";
                Field<ShellButton>(dialog, "renameButton").PerformClick();
            });
            check(store.ReadEntries().First(item => item.Url == original.Url && item.Kind == original.Kind).Name == "집 NAS"
                && File.ReadAllText(settingsPath) == settingsFile && core.Source == source
                && await core.ExecuteScriptAsync("document.cookie") == cookies
                && await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')") == storage,
                "Renaming inside the modal updates only history and preserves the NAS login storage");
            string renamedHistory = File.ReadAllText(historyPath);

            using (FileStream locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
                {
                    SelectRow(dialog, alternate);
                    Field<ShellButton>(dialog, "useButton").PerformClick();
                    check(dialog.Visible && dialog.DialogResult == DialogResult.None && dialog.SelectedConnection == null
                        && Field<Label>(dialog, "statusLabel").Text.Contains("저장하지 못")
                        && address.Input.ReadOnly && address.AddressText == original.Url && core.Source == source
                        && File.ReadAllText(settingsPath) == settingsFile && File.ReadAllText(historyPath) == renamedHistory,
                        "A connection-setting save failure keeps the modal open and leaves the previous connection and history intact");
                });
            }

            core = await BrowserFixtureNavigation.RunAsync(window, fixture, fixtureHosts, delegate
            {
                InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
                {
                    SelectRow(dialog, alternate);
                    Field<ShellButton>(dialog, "useButton").PerformClick();
                });
            }, alternate.Url, "NAS Desk local fixture");
            check(window.HistoryDialog == null && address.Input.ReadOnly && !address.TypePicker.Enabled
                && address.AddressText == alternate.Url && ConnectionSettings.Read(settingsPath).Url == alternate.Url
                && store.ReadEntries()[0].Name == "백업 NAS" && Object.ReferenceEquals(core, window.Browser.CoreWebView2)
                && core.Environment.UserDataFolder == profile,
                "Connecting a saved modal row immediately applies and loads it while keeping the main address read-only");

            const string added = "https://nas-desk-alternate.test/index.html?modal=new";
            core = await BrowserFixtureNavigation.RunAsync(window, fixture, fixtureHosts, delegate
            {
                InModal(window, delegate { Click(address.Input); }, delegate(AddressHistoryDialog dialog)
                {
                    Field<TextBox>(dialog, "addressInput").Text = added;
                    Field<TextBox>(dialog, "addNameInput").Text = "새 NAS";
                    Field<ShellButton>(dialog, "addButton").PerformClick();
                    check(address.AddressText == alternate.Url && address.Input.ReadOnly && core.Source == alternate.Url
                        && Field<ListBox>(dialog, "historyList").SelectedIndex == 0,
                        "Adding a URL inside the modal selects its saved record without changing the connected NAS");
                    Field<ShellButton>(dialog, "useButton").PerformClick();
                });
            }, added, "NAS Desk local fixture");
            check(address.AddressText == added && address.Input.ReadOnly && store.ReadEntries()[0].Name == "새 NAS",
                "A new NAS can be entered, named, saved and connected entirely inside the address history modal");

            core = await BrowserFixtureNavigation.RunAsync(window, fixture, fixtureHosts, delegate
            {
                InModal(window, delegate { address.HistoryButton.PerformClick(); }, delegate(AddressHistoryDialog dialog)
                {
                    SelectRow(dialog, original);
                    Field<ShellButton>(dialog, "useButton").PerformClick();
                });
            }, original.Url, "NAS Desk local fixture");
            check(address.Input.ReadOnly && address.AddressText == original.Url && core.Source == original.Url,
                "Returning to the original NAS through history never creates an editable main URL field");
        }

        internal static void InModal(MainWindow window, Action trigger, Action<AddressHistoryDialog> inspect)
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
                        failure = new TimeoutException("Connection modal did not become visible.");
                        foreach (AddressHistoryDialog open in Application.OpenForms.Cast<Form>().OfType<AddressHistoryDialog>().ToArray()) open.Close();
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
                timer.Start(); trigger(); timer.Stop();
            }
            if (failure != null) throw failure;
            if (!observed) throw new Exception("The address action did not open its connection modal.");
        }

        private static T Field<T>(AddressHistoryDialog dialog, string field) where T : class
        {
            return typeof(AddressHistoryDialog).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog) as T;
        }

        private static void SelectRow(AddressHistoryDialog dialog, ConnectionSettings target)
        {
            ListBox list = Field<ListBox>(dialog, "historyList");
            list.SelectedIndex = Enumerable.Range(0, list.Items.Count).First(index =>
            {
                object row = list.Items[index];
                ConnectionSettings connection = (ConnectionSettings)row.GetType()
                    .GetField("Connection", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(row);
                return connection.Url == target.Url && connection.Kind == target.Kind;
            });
        }

        private static void Click(Control control)
        {
            string mouseEvent = control is TextBox ? "OnMouseClick" : "OnMouseDown";
            typeof(Control).GetMethod(mouseEvent, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0) });
        }

        private static bool Command(Control control, Keys key)
        {
            Message message = Message.Create(control.Handle, 0x0100, (IntPtr)(key & Keys.KeyCode), IntPtr.Zero);
            return (bool)control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { message, key });
        }

    }
}
