using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal sealed class ConnectionSettings
    {
        public string Url { get; set; }

        public static bool IsValid(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps && !String.IsNullOrEmpty(uri.Host)
                && String.IsNullOrEmpty(uri.UserInfo);
        }

        public static bool IsInternal(string target, string home)
        {
            if (target == "about:blank") return true;
            if (target.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) target = target.Substring(5);
            Uri destination, origin;
            return Uri.TryCreate(target, UriKind.Absolute, out destination)
                && Uri.TryCreate(home, UriKind.Absolute, out origin)
                && destination.Scheme == Uri.UriSchemeHttps
                && destination.GetLeftPart(UriPartial.Authority).Equals(origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
                && String.IsNullOrEmpty(destination.UserInfo);
        }

        public static ConnectionSettings Read(string path)
        {
            if (!File.Exists(path)) return new ConnectionSettings();
            ConnectionSettings settings = new JavaScriptSerializer().Deserialize<ConnectionSettings>(File.ReadAllText(path));
            if (settings == null || !IsValid(settings.Url)) throw new InvalidDataException("NAS 주소는 https://로 시작하고 계정 정보를 포함하지 않아야 합니다.");
            return settings;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    internal static class Program
    {
        internal const string AppName = "Synology NAS Desktop";
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NasDesk");
        internal static readonly string SettingsPath = Path.Combine(DataDirectory, "connection.json");
        internal static readonly string ProfileDirectory = Path.Combine(DataDirectory, "WebView2");

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (Mutex instance = new Mutex(true, @"Local\NasDesk.Instance.v1", out first))
            using (EventWaitHandle activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\NasDesk.Activate.v1"))
            {
                if (!first) { activate.Set(); return; }
                try
                {
                    string path = File.Exists(SettingsPath) ? SettingsPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connection.local.json");
                    ConnectionSettings settings;
                    try { settings = ConnectionSettings.Read(path); }
                    catch (Exception error)
                    {
                        MessageBox.Show("연결 설정을 읽지 못했습니다. 주소를 다시 입력해 주세요.\n\n" + error.Message, AppName);
                        settings = new ConnectionSettings();
                    }
                    if (!ConnectionSettings.IsValid(settings.Url))
                    {
                        string address = AddressDialog.Ask(null, settings.Url);
                        if (address == null) return;
                        settings.Url = address;
                        settings.Save(SettingsPath);
                    }
                    using (MainWindow window = new MainWindow(settings, ProfileDirectory, false))
                    {
                        RegisteredWaitHandle watcher = ThreadPool.RegisterWaitForSingleObject(activate, delegate
                        {
                            try
                            {
                                if (window.IsHandleCreated && !window.IsDisposed)
                                    window.BeginInvoke((Action)window.RestoreFromTray);
                            }
                            catch (InvalidOperationException) { }
                        }, null, Timeout.Infinite, false);
                        try { Application.Run(window); }
                        finally { watcher.Unregister(null); }
                    }
                }
                catch (Exception error)
                {
                    MessageBox.Show("앱을 시작하지 못했습니다.\n\n" + error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    internal sealed class AddressDialog : Form
    {
        private readonly TextBox address;
        private AddressDialog(string current)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "NAS 접속 주소";
            Font = new Font("Malgun Gothic", 9F);
            ClientSize = new Size(650, 242);
            BackColor = NasTheme.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Label title = new Label { Text = "내 NAS에 연결", AutoSize = true, ForeColor = NasTheme.Text, Font = new Font("Malgun Gothic", 17F, FontStyle.Bold), Location = new Point(22, 16) };
            Label label = new Label { Text = "이 PC에서 인증서 경고 없이 열리는 NAS의 직접 HTTPS 주소를 입력하세요.", AutoSize = true, ForeColor = NasTheme.Muted, Location = new Point(25, 62) };
            address = new TextBox { Text = current ?? "https://", Location = new Point(25, 96), Width = 600, BackColor = NasTheme.Surface, ForeColor = NasTheme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11F) };
            Label hint = new Label { Text = "예: https://nas.example.com:5001/\nQuickConnect 시작 주소 대신 연결 완료 후의 직접 주소를 사용하세요.", ForeColor = NasTheme.Muted, Location = new Point(25, 132), Size = new Size(600, 36), Font = new Font("Malgun Gothic", 8.5F) };
            ShellButton save = new ShellButton { Text = "연결", Glyph = ShellGlyph.ArrowRight, Primary = true, Location = new Point(514, 182), Width = 111, Height = 38 };
            ShellButton cancel = new ShellButton { Text = "취소", Location = new Point(391, 182), Width = 111, Height = 38, DialogResult = DialogResult.Cancel };
            save.Click += delegate
            {
                if (!ConnectionSettings.IsValid(address.Text.Trim()))
                {
                    MessageBox.Show(this, "https://로 시작하는 NAS 주소를 입력하세요. 아이디와 비밀번호는 로그인 화면에서 입력합니다.", Program.AppName);
                    return;
                }
                DialogResult = DialogResult.OK;
            };
            Controls.AddRange(new Control[] { title, label, address, hint, save, cancel });
            NasTheme.ApplyWindowChrome(this);
            AcceptButton = save;
            CancelButton = cancel;
            ResumeLayout(true);
        }

        internal static string Ask(IWin32Window owner, string current)
        {
            using (AddressDialog dialog = new AddressDialog(current))
                return dialog.ShowDialog(owner) == DialogResult.OK ? new Uri(dialog.address.Text.Trim()).AbsoluteUri : null;
        }
    }
}
