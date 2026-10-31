using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NasDesk
{
    internal static class Program
    {
        internal const string AppName = "NAS Desktop";
        // Keep the original neutral data/IPC names so existing sessions survive renaming.
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NasDesk");
        internal static readonly string SettingsPath = Path.Combine(DataDirectory, "connection.json");
        internal static readonly string AppearancePath = Path.Combine(DataDirectory, "appearance.json");
        internal static readonly string HistoryPath = Path.Combine(DataDirectory, "address-history.json");
        internal static readonly string ProfileDirectory = Path.Combine(DataDirectory, "WebView2");

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppearanceSettings appearance = AppearanceSettings.ReadPreferences(AppearancePath);
            NasTheme.SetAppearance(appearance.Theme, appearance.SelectedMode);
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
                        settings = AddressDialog.Ask(null, settings);
                        if (settings == null) return;
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
        private readonly ConnectionTypePicker typePicker;
        private ThemePalette appliedPalette;
        private ConnectionSettings selectedSettings;
        private AddressDialog(ConnectionSettings current)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            appliedPalette = NasTheme.Current;
            Text = "NAS 접속 주소";
            Font = new Font("Malgun Gothic", 9F);
            ClientSize = new Size(650, 280);
            BackColor = NasTheme.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Label title = new Label { Text = "내 NAS에 연결", AutoSize = true, ForeColor = NasTheme.Text, Font = new Font("Malgun Gothic", 17F, FontStyle.Bold), Location = new Point(22, 16) };
            Label label = new Label { Text = "NAS의 접속 방식을 선택하고 관리 주소를 입력하세요.", AutoSize = true, ForeColor = NasTheme.Muted, Location = new Point(25, 62) };
            typePicker = new ConnectionTypePicker { Location = new Point(25, 94), Size = new Size(154, 34), Kind = current.Kind };
            address = new TextBox { Text = current.Url ?? "", Location = new Point(191, 97), Width = 434, BackColor = NasTheme.Surface, ForeColor = NasTheme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11F) };
            Label hint = new Label { ForeColor = NasTheme.Muted, Location = new Point(25, 143), Size = new Size(600, 54), Font = new Font("Malgun Gothic", 8.5F) };
            Action updateHint = delegate
            {
                bool quickConnect = typePicker.Kind == ConnectionKind.SynologyQuickConnect;
                hint.Text = quickConnect
                    ? "예: my-nas / quickconnect.to/my-nas\nSynology QuickConnect ID로 연결 경로를 자동 확인합니다."
                    : "예: https://nas.example.com/ / nas.local:443 / https://192.0.2.10/\nQNAP·ASUSTOR·TrueNAS 등 NAS의 HTTPS 관리 주소를 입력하세요.\n신뢰할 수 있는 HTTPS 인증서가 필요합니다.";
                address.AccessibleName = quickConnect ? "Synology QuickConnect ID 또는 주소" : "NAS HTTPS 관리 주소";
            };
            typePicker.KindChanged += delegate { updateHint(); };
            updateHint();
            ShellButton save = new ShellButton { Text = "연결", Glyph = ShellGlyph.ArrowRight, Primary = true, Location = new Point(514, 220), Width = 111, Height = 38 };
            ShellButton cancel = new ShellButton { Text = "취소", Location = new Point(391, 220), Width = 111, Height = 38, DialogResult = DialogResult.Cancel };
            save.Click += delegate
            {
                ConnectionSettings candidate;
                if (!ConnectionSettings.TryCreate(address.Text.Trim(), typePicker.Kind, out candidate))
                {
                    MessageBox.Show(this, typePicker.Kind == ConnectionKind.SynologyQuickConnect
                        ? "Synology QuickConnect ID 또는 quickconnect.to/ID 주소를 입력하세요."
                        : "NAS의 HTTPS 관리 주소를 입력하세요. 아이디와 비밀번호는 NAS 로그인 화면에서 입력합니다.", Program.AppName);
                    return;
                }
                selectedSettings = candidate;
                DialogResult = DialogResult.OK;
            };
            Controls.AddRange(new Control[] { title, label, typePicker, address, hint, save, cancel });
            NasTheme.ApplyWindowChrome(this);
            AcceptButton = save;
            CancelButton = cancel;
            NasTheme.Changed += OnThemeChanged;
            ResumeLayout(true);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            MainWindow.RecolorControls(this, appliedPalette);
            appliedPalette = NasTheme.Current;
            NasTheme.RefreshWindowChrome(this);
            Invalidate(true);
        }

        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            base.WndProc(ref message);
            if (kind == 0x001A || kind == 0x031A || kind == 0x0015)
            {
                NasTheme.RefreshSystemAppearance();
                NasTheme.RefreshWindowChrome(this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) NasTheme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        internal static ConnectionSettings Ask(IWin32Window owner, ConnectionSettings current)
        {
            using (AddressDialog dialog = new AddressDialog(current))
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.selectedSettings : null;
        }
    }
}
