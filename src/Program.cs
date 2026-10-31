using System;
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
        internal static readonly string SecurityPath = Path.Combine(DataDirectory, "security.json");

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
                    try
                    {
                        settings = ConnectionSettings.Read(path);
                        // Only migrate the per-user application data, never the
                        // developer's optional bundled settings file.
                        if (path == SettingsPath) ConnectionSettings.ProtectIfNeeded(path);
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show("연결 설정을 읽지 못했습니다. 주소를 다시 입력해 주세요.\n\n" + error.Message, AppName);
                        settings = new ConnectionSettings();
                    }
                    try { new AddressHistoryStore(HistoryPath).ProtectIfNeeded(); }
                    catch (Exception error) { MessageBox.Show("주소 기록을 보호하지 못했습니다. 기존 파일은 유지합니다.\n\n" + error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    if (!ConnectionSettings.IsValid(settings.Url))
                    {
                        AddressHistoryStore history = new AddressHistoryStore(HistoryPath);
                        using (AddressHistoryDialog dialog = new AddressHistoryDialog(history, settings))
                        {
                            dialog.ConnectRequested = delegate(ConnectionSettings candidate)
                            {
                                try { candidate.Save(SettingsPath); }
                                catch (Exception error)
                                {
                                    dialog.ShowConnectionError("연결 주소를 저장하지 못했습니다: " + error.Message);
                                    return false;
                                }
                                settings = candidate;
                                try { if (!ConnectionAddress.ContainsSensitiveToken(candidate.Url)) history.Add(candidate); }
                                catch (IOException) { }
                                catch (UnauthorizedAccessException) { }
                                return true;
                            };
                            if (dialog.ShowDialog() != DialogResult.OK) return;
                        }
                    }
                    using (MainWindow window = new MainWindow(settings, ProfileDirectory, false))
                    {
                        RegisteredWaitHandle watcher = ThreadPool.RegisterWaitForSingleObject(activate, delegate
                        {
                            try
                            {
                                if (window.IsHandleCreated && !window.IsDisposed)
                                    window.BeginInvoke((Action)window.RestoreWindow);
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

}
