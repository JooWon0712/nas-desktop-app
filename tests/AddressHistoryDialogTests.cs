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
                TextBox addName = Field<TextBox>(dialog, "addNameInput");
                TextBox selectedName = Field<TextBox>(dialog, "selectedNameInput");
                ConnectionTypePicker picker = Field<ConnectionTypePicker>(dialog, "typePicker");
                ShellButton add = Field<ShellButton>(dialog, "addButton");
                ShellButton remove = Field<ShellButton>(dialog, "removeButton");
                ShellButton rename = Field<ShellButton>(dialog, "renameButton");
                ShellButton use = Field<ShellButton>(dialog, "useButton");
                Label status = Field<Label>(dialog, "statusLabel");
                check(!File.Exists(path) && list.Items.Count == 0 && !remove.Enabled && !use.Enabled
                    && dialog.SelectedConnection == null && input.Text == current.Url
                    && addName.Text == String.Empty && !rename.Enabled && !selectedName.Enabled,
                    "Opening empty address history does not write or add the current NAS");

                input.Text = "https://first-nas.test:8443/";
                addName.Text = "집 NAS";
                add.PerformClick();
                check(store.Read().Count == 1 && list.Items.Count == 1 && list.SelectedIndex == 0
                    && dialog.SelectedConnection == null && current.Url == "https://current-nas.test/"
                    && store.ReadEntries()[0].Name == "집 NAS" && selectedName.Text == "집 NAS"
                    && list.Items[0].ToString().StartsWith("집 NAS · HTTPS 주소 · "),
                    "Manual history addition stores a named NAS without changing the current connection");
                addName.Text = String.Empty;
                add.PerformClick();
                check(store.Read().Count == 1 && list.Items.Count == 1 && list.SelectedIndex == 0
                    && store.ReadEntries()[0].Name == "집 NAS" && selectedName.Text == "집 NAS",
                    "Adding an existing address with a blank optional name preserves its saved name");

                picker.Kind = ConnectionKind.SynologyQuickConnect;
                input.Text = "team-nas";
                addName.Text = "회사 NAS";
                add.PerformClick();
                check(store.Read().Count == 2 && list.Items.Count == 2 && list.SelectedIndex == 0
                    && list.Items[0].ToString().Contains("Synology QuickConnect")
                    && store.Read()[0].Kind == ConnectionKind.SynologyQuickConnect
                    && list.Items[0].ToString().StartsWith("회사 NAS · ") && selectedName.Text == "회사 NAS",
                    "The history list displays the NAS name, explicit QuickConnect type and normalized URL");

                list.SelectedIndex = 1;
                check(selectedName.Text == "집 NAS" && rename.Enabled && selectedName.Enabled,
                    "Selecting a saved NAS populates its separate name editor");
                selectedName.Text = "  집 파일 서버  ";
                rename.PerformClick();
                check(store.ReadEntries()[0].Name == "회사 NAS" && store.ReadEntries()[1].Name == "집 파일 서버"
                    && list.SelectedIndex == 1 && selectedName.Text == "집 파일 서버"
                    && list.Items[1].ToString().StartsWith("집 파일 서버 · ")
                    && input.Text == "https://team-nas.quickconnect.to/" && addName.Text == "회사 NAS"
                    && current.Url == "https://current-nas.test/" && dialog.SelectedConnection == null,
                    "Saving a NAS name trims it without reordering history, changing address drafts or navigating");
                string beforeReopen = File.ReadAllText(path);
                using (AddressHistoryDialog reopened = new AddressHistoryDialog(store, store.Read()[1]))
                {
                    check(Field<TextBox>(reopened, "selectedNameInput").Text == "집 파일 서버"
                        && File.ReadAllText(path) == beforeReopen,
                        "A renamed NAS is restored when reopening history without rewriting the file");
                }

                picker.Kind = ConnectionKind.DirectHttps;
                input.Text = "http://invalid-history.test/";
                add.PerformClick();
                check(list.Items.Count == 2 && store.Read().Count == 2 && input.Text == "http://invalid-history.test/"
                    && status.Text.Contains("HTTPS"),
                    "Invalid history input stays available for correction without changing saved rows");

                input.Text = "https://name-validation.test/";
                addName.Text = new String('n', 61);
                string invalidNameRows = Rows(list);
                add.PerformClick();
                check(list.Items.Count == 2 && Rows(list) == invalidNameRows && addName.Text.Length == 61
                    && store.Read().Count == 2 && status.Text.Contains("60자"),
                    "An invalid new NAS name preserves its draft and saved rows");
                selectedName.Text = new String('r', 61);
                rename.PerformClick();
                check(list.SelectedIndex == 1 && Rows(list) == invalidNameRows && selectedName.Text.Length == 61
                    && store.ReadEntries()[1].Name == "집 파일 서버" && status.Text.Contains("60자"),
                    "An invalid rename preserves the editor, selection and stored NAS name");

                list.SelectedIndex = 1;
                input.Text = "https://cannot-save-history.test/";
                addName.Text = "추가 중인 NAS";
                string rows = Rows(list);
                string file = File.ReadAllText(path);
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    add.PerformClick();
                check(list.SelectedIndex == 1 && Rows(list) == rows && input.Text == "https://cannot-save-history.test/"
                    && addName.Text == "추가 중인 NAS" && File.ReadAllText(path) == file && status.Text.Contains("저장하지 못"),
                    "A history save failure preserves the address, name draft, selected row and visible list");

                selectedName.Text = "저장 실패 이름";
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    rename.PerformClick();
                check(list.SelectedIndex == 1 && Rows(list) == rows && selectedName.Text == "저장 실패 이름"
                    && File.ReadAllText(path) == file && status.Text.Contains("이름을 저장하지 못"),
                    "A name save failure preserves the name editor, selection, visible rows and file");

                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    remove.PerformClick();
                check(list.SelectedIndex == 1 && Rows(list) == rows && File.ReadAllText(path) == file
                    && selectedName.Text == "저장 실패 이름" && status.Text.Contains("삭제하지 못"),
                    "A history delete failure preserves the selection and visible list");

                remove.PerformClick();
                check(store.Read().Count == 1 && list.Items.Count == 1 && list.SelectedIndex == 0
                    && current.Url == "https://current-nas.test/" && dialog.SelectedConnection == null
                    && selectedName.Text == "회사 NAS",
                    "Deleting a history entry keeps the current NAS and pending connection unchanged");
                selectedName.Text = "   ";
                rename.PerformClick();
                check(store.ReadEntries()[0].Name == String.Empty && selectedName.Text == String.Empty
                    && list.Items[0].ToString().StartsWith("team-nas.quickconnect.to · "),
                    "Saving an empty name clears the alias and displays the NAS host name");
                selectedName.Text = "회사 문서";
                rename.PerformClick();
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
                    && File.ReadAllText(path) == beforeCancel && store.ReadEntries()[0].Name == "회사 문서",
                    "Closing history returns no connection and preserves saved NAS names without rewriting records");
            }

            string legacyPath = Path.Combine(folder, "legacy-history.json");
            string legacy = "{\"Entries\":[{\"Url\":\"https://legacy-nas.test/\",\"Type\":\"https\"}]}";
            File.WriteAllText(legacyPath, legacy);
            using (AddressHistoryDialog dialog = new AddressHistoryDialog(new AddressHistoryStore(legacyPath), null))
            {
                ListBox list = Field<ListBox>(dialog, "historyList");
                check(list.Items.Count == 1 && list.Items[0].ToString().StartsWith("legacy-nas.test · HTTPS 주소 · ")
                    && Field<TextBox>(dialog, "selectedNameInput").Text == String.Empty
                    && File.ReadAllText(legacyPath) == legacy,
                    "Legacy history without NAS names displays host names and remains unchanged on opening");
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
