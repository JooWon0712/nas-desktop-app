using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NasDesk
{
    internal sealed class MainWindow : Form
    {
        private readonly ConnectionSettings settings;
        private readonly string profilePath;
        private readonly bool testMode;
        private readonly bool popup;
        private readonly List<MainWindow> children = new List<MainWindow>();
        private readonly List<MainWindow> hiddenChildren = new List<MainWindow>();
        private readonly NotifyIcon tray;
        private readonly ToolStripStatusLabel status;
        private readonly Label connectionBadge;
        private readonly Label connectionHost;
        private readonly Panel errorPanel;
        private readonly Label errorText;
        private readonly Icon appIcon;
        private bool exitRequested;
        private bool balloonShown;
        private CoreWebView2Environment environment;
        internal readonly WebView2 Browser;
        internal bool Ready { get; private set; }
        internal Exception InitializationError { get; private set; }
        internal bool TrayVisible { get { return tray != null && tray.Visible; } }

        internal MainWindow(ConnectionSettings settings, string profilePath, bool testMode)
            : this(settings, profilePath, testMode, false) { }

        private MainWindow(ConnectionSettings settings, string profilePath, bool testMode, bool popup)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            this.settings = settings;
            this.profilePath = profilePath;
            this.testMode = testMode;
            this.popup = popup;
            Text = Program.AppName;
            Font = new Font("Malgun Gothic", 9F);
            Size = new Size(1320, 900);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            if (testMode) { Opacity = 0; ShowInTaskbar = false; }
            appIcon = NasTheme.CreateIcon();
            Icon = appIcon;
            BackColor = NasTheme.Background;

            Browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(24, 27, 32) };
            Panel top = new Panel { Dock = DockStyle.Top, Height = 144, Padding = new Padding(20, 0, 20, 10), BackColor = NasTheme.Background };
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 80, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            Panel brand = new Panel { Dock = DockStyle.Fill };
            ShellLogo logo = new ShellLogo { Location = new Point(0, 18), Size = new Size(44, 44) };
            Label brandTitle = new Label { Text = "Synology NAS", Font = new Font("Segoe UI", 17F, FontStyle.Bold), ForeColor = NasTheme.Text, AutoSize = true, Location = new Point(56, 13) };
            Label brandSubtitle = new Label { Text = "D E S K T O P   W O R K S P A C E", Font = new Font("Segoe UI", 7.3F), ForeColor = NasTheme.Muted, AutoSize = true, Location = new Point(59, 48) };
            brand.Controls.AddRange(new Control[] { logo, brandTitle, brandSubtitle });
            Panel connection = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 24, 0, 21) };
            connectionBadge = new Label { Text = "●  연결 준비", AutoSize = false, Dock = DockStyle.Right, Width = 115, TextAlign = ContentAlignment.MiddleCenter, BackColor = NasTheme.Surface, ForeColor = NasTheme.Muted, Font = new Font("Malgun Gothic", 9F, FontStyle.Bold) };
            connectionHost = new Label { Text = new Uri(settings.Url).Authority, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = NasTheme.Muted, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 16, 0), Font = new Font("Segoe UI", 9F) };
            connection.Controls.Add(connectionHost);
            connection.Controls.Add(connectionBadge);
            header.Controls.Add(brand, 0, 0);
            header.Controls.Add(connection, 1, 0);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 44, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            FlowLayoutPanel left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            FlowLayoutPanel right = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
            AddButton(left, "NAS 홈", ShellGlyph.Home, true, delegate { NavigateHome(); });
            AddButton(left, "새로 고침", ShellGlyph.Refresh, false, delegate { ReloadPage(); });
            AddButton(left, "다운로드", ShellGlyph.Download, false, delegate { if (Ready) Browser.CoreWebView2.OpenDefaultDownloadDialog(); });
            if (!popup) AddButton(right, "숨기기", ShellGlyph.Tray, false, delegate { HideToTray(); });
            if (!popup) AddButton(right, "접속 설정", ShellGlyph.Settings, false, delegate { ChangeAddress(); });
            AddButton(right, "브라우저", ShellGlyph.External, false, delegate { OpenExternal(settings.Url, false); });
            actions.Controls.Add(left, 0, 0);
            actions.Controls.Add(right, 1, 0);
            top.Controls.Add(actions);
            top.Controls.Add(header);
            StatusStrip footer = new StatusStrip { BackColor = NasTheme.Background, ForeColor = NasTheme.Muted, SizingGrip = false, Padding = new Padding(20, 4, 20, 4) };
            status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = NasTheme.Muted, Text = "보안 연결을 준비하고 있습니다." };
            footer.Items.Add(status);
            footer.Items.Add(new ToolStripStatusLabel { Text = "X로 닫아도 트레이에서 계속 실행", ForeColor = NasTheme.Muted });

            errorPanel = new Panel { Dock = DockStyle.Fill, BackColor = NasTheme.Surface, Visible = false };
            Label errorTitle = new Label { Text = "연결을 확인해 주세요", Font = new Font("Malgun Gothic", 20F, FontStyle.Bold), ForeColor = NasTheme.Text, AutoSize = true, Location = new Point(36, 45) };
            errorText = new Label { AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new Point(38, 110), Size = new Size(620, 155), ForeColor = NasTheme.Muted, Font = new Font("Malgun Gothic", 10F), Text = "연결을 준비하고 있습니다." };
            ShellButton retry = new ShellButton { Text = "다시 연결", Glyph = ShellGlyph.Refresh, Primary = true, Width = 144, Height = 42, Location = new Point(38, 280) };
            retry.Click += async delegate { if (Ready) NavigateHome(); else await InitializeAsync(null); };
            errorPanel.Controls.Add(retry);
            errorPanel.Controls.Add(errorText);
            errorPanel.Controls.Add(errorTitle);
            Panel viewport = new Panel { Dock = DockStyle.Fill, BackColor = NasTheme.Border, Padding = new Padding(1) };
            Panel workspace = new Panel { Dock = DockStyle.Fill, BackColor = NasTheme.Background, Padding = new Padding(20, 0, 20, 0) };
            viewport.Controls.Add(Browser);
            viewport.Controls.Add(errorPanel);
            workspace.Controls.Add(viewport);
            Controls.Add(workspace);
            Controls.Add(top);
            Controls.Add(footer);
            NasTheme.ApplyWindowChrome(this);
            errorPanel.Resize += delegate { errorText.Width = Math.Max(200, errorPanel.ClientSize.Width - 76); };

            if (!popup)
            {
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.BackColor = NasTheme.Surface;
                menu.ForeColor = NasTheme.Text;
                menu.Font = new Font("Malgun Gothic", 9F);
                menu.Items.Add("NAS 창 열기", null, delegate { RestoreFromTray(); });
                menu.Items.Add("새로 고침", null, delegate { ReloadPage(); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("완전 종료", null, delegate { RequestExit(); });
                tray = new NotifyIcon { Text = Program.AppName, Icon = appIcon, ContextMenuStrip = menu, Visible = true };
                tray.DoubleClick += delegate { RestoreFromTray(); };
                tray.BalloonTipClicked += delegate { RestoreFromTray(); };
                Shown += async delegate { await InitializeAsync(null); };
            }
            ResumeLayout(true);
        }

        private static void AddButton(FlowLayoutPanel strip, string text, ShellGlyph glyph, bool primary, Action action)
        {
            ShellButton button = new ShellButton { Text = text, Glyph = glyph, Primary = primary, Size = new Size(108, 38), Margin = new Padding(0, 2, 7, 0) };
            button.Click += delegate { action(); };
            strip.Controls.Add(button);
        }

        private void SetConnectionState(string text, Color color)
        {
            connectionBadge.Text = "●  " + text;
            connectionBadge.ForeColor = color;
        }

        internal async Task InitializeAsync(CoreWebView2Environment sharedEnvironment)
        {
            if (Ready || IsDisposed || Disposing) return;
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
                environment = sharedEnvironment ?? await CoreWebView2Environment.CreateAsync(null, profilePath);
                if (IsDisposed || Disposing) return;
                await Browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing) return;
                CoreWebView2 core = Browser.CoreWebView2;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (!ConnectionSettings.IsInternal(e.Uri, settings.Url))
                    {
                        e.Cancel = true;
                        status.Text = "다른 주소로의 이동이 차단됐습니다. 필요하면 ‘브라우저로 열기’를 사용하세요.";
                        return;
                    }
                    errorPanel.Visible = false;
                    status.Text = "NAS에 안전하게 연결하고 있습니다.";
                    SetConnectionState("연결 중", NasTheme.Muted);
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.IsSuccess)
                    {
                        errorPanel.Visible = false;
                        status.Text = "HTTPS · " + new Uri(settings.Url).Authority;
                        SetConnectionState("연결됨", NasTheme.Accent);
                    }
                    else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        ShowError("NAS에 연결하지 못했습니다: " + e.WebErrorStatus + "\n네트워크, NAS 전원, 접속 주소를 확인한 뒤 다시 연결하세요. 인증서 오류는 브라우저에서도 확인해 주세요.");
                };
                core.NewWindowRequested += OnNewWindowRequested;
                core.WindowCloseRequested += delegate { if (popup) RequestExit(); };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e)
                {
                    // Notifications are useful while resident; leave permission decisions in the web UI.
                    if (!ConnectionSettings.IsInternal(e.Uri, settings.Url)) e.State = CoreWebView2PermissionState.Deny;
                };
                core.LaunchingExternalUriScheme += delegate(object sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e) { e.Cancel = true; };
                core.ProcessFailed += delegate
                {
                    Ready = false;
                    ShowError("웹 화면 프로세스가 중단됐습니다. 트레이 메뉴에서 완전 종료한 뒤 앱을 다시 실행하세요.");
                };
                InitializationError = null;
                Ready = true;
                if (!popup)
                {
                    if (testMode) core.NavigateToString("<!doctype html><title>NAS Desk test</title><p>Local test only</p>");
                    else NavigateHome();
                }
            }
            catch (Exception error)
            {
                if (IsDisposed || Disposing || exitRequested) return;
                InitializationError = error;
                ShowError("웹 화면을 준비하지 못했습니다. Microsoft Edge WebView2 Runtime 설치 상태를 확인하세요.\n\n" + error.Message);
            }
        }

        private async void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (exitRequested || IsDisposed || Disposing) return;
            if (!ConnectionSettings.IsInternal(e.Uri, settings.Url))
            {
                if (e.IsUserInitiated) OpenExternal(e.Uri, true);
                else status.Text = "다른 사이트의 자동 팝업을 차단했습니다.";
                return;
            }
            CoreWebView2Deferral deferral = e.GetDeferral();
            MainWindow child = null;
            try
            {
                child = new MainWindow(settings, profilePath, testMode, true);
                // Force HWND creation without displaying an uninitialized window.
                IntPtr handle = child.Handle;
                await child.InitializeAsync(environment);
                if (IsDisposed || exitRequested || !child.Ready) { child.Dispose(); return; }
                e.NewWindow = child.Browser.CoreWebView2;
                children.Add(child);
                MainWindow trackedChild = child;
                child.FormClosed += delegate { children.Remove(trackedChild); hiddenChildren.Remove(trackedChild); };
                if (Visible) child.Show();
                else hiddenChildren.Add(child);
            }
            catch (Exception error)
            {
                if (child != null) child.Dispose();
                if (!IsDisposed) status.Text = "새 창을 열지 못했습니다: " + error.Message;
            }
            finally
            {
                try { deferral.Complete(); }
                catch (InvalidOperationException) { }
                catch (System.Runtime.InteropServices.COMException) { }
            }
        }

        private void ShowError(string message)
        {
            errorText.Text = message;
            errorPanel.Visible = true;
            errorPanel.BringToFront();
            status.Text = "연결 확인 필요 · 접속 주소 / 다시 연결";
            SetConnectionState("연결 확인", Color.FromArgb(244, 184, 96));
        }

        private void NavigateHome()
        {
            if (!Ready) return;
            errorPanel.Visible = false;
            Browser.CoreWebView2.Navigate(settings.Url);
        }

        private void ReloadPage()
        {
            if (!Ready) return;
            if (errorPanel.Visible) NavigateHome();
            else Browser.Reload();
        }

        private void ChangeAddress()
        {
            string address = AddressDialog.Ask(this, settings.Url);
            if (address == null) return;
            try
            {
                new ConnectionSettings { Url = address }.Save(Program.SettingsPath);
                foreach (MainWindow child in children.ToArray()) child.Close();
                settings.Url = address;
                connectionHost.Text = new Uri(settings.Url).Authority;
                NavigateHome();
            }
            catch (Exception error) { MessageBox.Show(this, "주소를 저장하지 못했습니다.\n" + error.Message, Program.AppName); }
        }

        private void OpenExternal(string value, bool confirm)
        {
            if (!ConnectionSettings.IsValid(value)) return;
            if (confirm && MessageBox.Show(this, "이 주소를 기본 브라우저에서 열까요?\n\n" + value, Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Process.Start(new ProcessStartInfo(value) { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, "브라우저를 열지 못했습니다.\n" + error.Message, Program.AppName); }
        }

        internal void HideToTray()
        {
            if (!TrayVisible) return;
            foreach (MainWindow child in children)
            {
                if (child.Visible && !hiddenChildren.Contains(child)) hiddenChildren.Add(child);
                child.HideToTrayChildren();
                child.Hide();
            }
            Hide();
            // Keep WebView alive; no TrySuspend, cache deletion, or logout on hide.
            if (!balloonShown && !testMode)
            {
                balloonShown = true;
                tray.ShowBalloonTip(3000, Program.AppName, "트레이에서 계속 실행 중입니다. 더블클릭하면 다시 열립니다. 종료하려면 우클릭 → 완전 종료를 선택하세요.", ToolTipIcon.Info);
            }
        }

        private void HideToTrayChildren()
        {
            foreach (MainWindow child in children)
            {
                if (child.Visible && !hiddenChildren.Contains(child)) hiddenChildren.Add(child);
                child.HideToTrayChildren();
                child.Hide();
            }
        }

        internal void RestoreFromTray()
        {
            if (IsDisposed || exitRequested) return;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            foreach (MainWindow child in hiddenChildren.ToArray()) if (!child.IsDisposed) child.RestoreFromTray();
            hiddenChildren.Clear();
        }

        internal static bool ShouldHide(CloseReason reason, bool exiting, bool trayAvailable, bool isPopup)
        {
            return reason == CloseReason.UserClosing && !exiting && trayAvailable && !isPopup;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (ShouldHide(e.CloseReason, exitRequested, TrayVisible, popup))
            {
                e.Cancel = true;
                HideToTray();
            }
            else
            {
                exitRequested = true;
                foreach (MainWindow child in children.ToArray()) child.RequestExit();
            }
            base.OnFormClosing(e);
        }

        internal void RequestExit()
        {
            exitRequested = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (MainWindow child in children.ToArray()) child.Dispose();
                if (tray != null)
                {
                    tray.Visible = false;
                    if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                    tray.Dispose();
                }
                Browser.Dispose();
                appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
