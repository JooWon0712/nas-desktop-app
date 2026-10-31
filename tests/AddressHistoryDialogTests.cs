using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace NasDesk
{
    internal static class AddressHistoryDialogTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string folder = Path.Combine(output, "history-dialog-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "history.json");
            AddressHistoryStore store = new AddressHistoryStore(path);
            ConnectionSettings current;
            ConnectionSettings.TryCreate("https://current-nas.test/", ConnectionKind.DirectHttps, out current);
            using (AddressHistoryDialog dialog = new AddressHistoryDialog(store, current))
            {
                dialog.Opacity = 0;
                dialog.Show();
                ListBox list = Field<ListBox>(dialog, "historyList");
                TextBox input = Field<TextBox>(dialog, "addressInput");
                ConnectionTypePicker picker = Field<ConnectionTypePicker>(dialog, "typePicker");
                ShellButton add = Field<ShellButton>(dialog, "addButton");
                ShellButton remove = Field<ShellButton>(dialog, "removeButton");
                ShellButton use = Field<ShellButton>(dialog, "useButton");
                Label status = Field<Label>(dialog, "statusLabel");
                check(!File.Exists(path) && list.Items.Count == 0 && !remove.Enabled && !use.Enabled
                    && dialog.SelectedConnection == null && input.Text == current.Url,
                    "Opening empty address history does not write or add the current NAS");

                input.Text = "https://first-nas.test:8443/";
                add.PerformClick();
                check(store.Read().Count == 1 && list.Items.Count == 1 && list.SelectedIndex == 0
                    && dialog.SelectedConnection == null && current.Url == "https://current-nas.test/",
                    "Manual history addition stores an address without selecting a connection or changing the current NAS");
                add.PerformClick();
                check(store.Read().Count == 1 && list.Items.Count == 1 && list.SelectedIndex == 0,
                    "Adding an existing address refreshes and selects its single history row");

                picker.Kind = ConnectionKind.SynologyQuickConnect;
                input.Text = "team-nas";
                add.PerformClick();
                check(store.Read().Count == 2 && list.Items.Count == 2 && list.SelectedIndex == 0
                    && list.Items[0].ToString().Contains("Synology QuickConnect")
                    && store.Read()[0].Kind == ConnectionKind.SynologyQuickConnect,
                    "The history list displays the explicit QuickConnect type and normalized URL");

                picker.Kind = ConnectionKind.DirectHttps;
                input.Text = "http://invalid-history.test/";
                add.PerformClick();
                check(list.Items.Count == 2 && store.Read().Count == 2 && input.Text == "http://invalid-history.test/"
                    && status.Text.Contains("HTTPS"),
                    "Invalid history input stays available for correction without changing saved rows");

                list.SelectedIndex = 1;
                input.Text = "https://cannot-save-history.test/";
                string rows = Rows(list);
                string file = File.ReadAllText(path);
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    add.PerformClick();
                check(list.SelectedIndex == 1 && Rows(list) == rows && input.Text == "https://cannot-save-history.test/"
                    && File.ReadAllText(path) == file && status.Text.Contains("저장하지 못"),
                    "A history save failure preserves the draft, selected row and visible list");

                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    remove.PerformClick();
                check(list.SelectedIndex == 1 && Rows(list) == rows && File.ReadAllText(path) == file
                    && status.Text.Contains("삭제하지 못"),
                    "A history delete failure preserves the selection and visible list");

                remove.PerformClick();
                check(store.Read().Count == 1 && list.Items.Count == 1 && list.SelectedIndex == 0
                    && current.Url == "https://current-nas.test/" && dialog.SelectedConnection == null,
                    "Deleting a history entry keeps the current NAS and pending connection unchanged");
                use.PerformClick();
                check(dialog.DialogResult == DialogResult.OK && dialog.SelectedConnection != null
                    && dialog.SelectedConnection.Kind == ConnectionKind.SynologyQuickConnect
                    && dialog.SelectedConnection.Url == "https://team-nas.quickconnect.to/",
                    "Using a history row returns its normalized address and explicit type");
            }

            string beforeCancel = File.ReadAllText(path);
            using (AddressHistoryDialog dialog = new AddressHistoryDialog(store, current))
            {
                dialog.Opacity = 0;
                dialog.Show();
                Field<ShellButton>(dialog, "closeButton").PerformClick();
                check(dialog.DialogResult == DialogResult.Cancel && dialog.SelectedConnection == null
                    && File.ReadAllText(path) == beforeCancel,
                    "Closing history returns no connection and does not rewrite saved records");
            }
        }

        private static T Field<T>(object instance, string name) where T : class
        {
            return typeof(AddressHistoryDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance) as T;
        }

        private static string Rows(ListBox list)
        {
            string value = String.Empty;
            foreach (object item in list.Items) value += item.ToString() + "\n";
            return value;
        }
    }
}
