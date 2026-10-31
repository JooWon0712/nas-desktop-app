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
        internal readonly AddressBar Address;
        private readonly string settingsPath;
        private readonly Panel errorPanel;
        private readonly Label errorText;
        private readonly Panel viewport;
        private readonly Icon appIcon;
        private readonly ToolTip toolTips = new ToolTip();
        private bool exitRequested;
        private bool balloonShown;
        private CoreWebView2Environment environment;
        private Task initializationTask;
        private TaskCompletionSource<bool> browserExitSignal;
        private bool browserNeedsRecreation;
        private bool waitForBrowserExit;
        private string lastLoadedUrl;
        private ConnectionSession connection;
        private Task navigationCheck;
        private string pendingNavigationUrl;
        private int navigationRequest;
        internal WebView2 Browser { get; private set; }
        internal bool Ready { get; private set; }
        internal Exception InitializationError { get; private set; }
        internal bool TrayVisible { get { return tray != null && tray.Visible; } }

        internal MainWindow(ConnectionSettings settings, string profilePath, bool testMode)
            : this(settings, profilePath, testMode, false, Program.SettingsPath) { }

        internal MainWindow(ConnectionSettings settings, string profilePath, bool testMode, string settingsPath)
            : this(settings, profilePath, testMode, false, settingsPath) { }

        internal MainWindow(ConnectionSettings settings, string profilePath, bool testMode, string settingsPath, ConnectionSession session)
            : this(settings, profilePath, testMode, false, settingsPath, session) { }

        private MainWindow(ConnectionSettings settings, string profilePath, bool testMode, bool popup, string settingsPath, ConnectionSession session = null)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            this.settings = settings;
            this.profilePath = profilePath;
            this.testMode = testMode;
            this.popup = popup;
            this.settingsPath = settingsPath;
            connection = session ?? new ConnectionSession(settings.Url);
            Text = Program.AppName;
            Font = new Font("Malgun Gothic", 9F);
            Size = new Size(1320, 900);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            if (testMode) { Opacity = 0; ShowInTaskbar = false; }
            appIcon = NasTheme.CreateIcon();
            Icon = appIcon;
            BackColor = NasTheme.Background;

            Browser = CreateBrowser();
            Panel top = new Panel { Dock = DockStyle.Top, Height = 148, Padding = new Padding(20, 8, 20, 14), BackColor = NasTheme.Background };
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 72, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            Panel brand = new Panel { Dock = DockStyle.Fill };
            ShellLogo logo = new ShellLogo { Location = new Point(0, 18), Size = new Size(44, 44) };
            Label brandTitle = new Label { Text = "Synology NAS", Font = new Font("Segoe UI", 17F, FontStyle.Bold), ForeColor = NasTheme.Text, AutoSize = true, Location = new Point(56, 13) };
            Label brandSubtitle = new Label { Text = "D E S K T O P   W O R K S P A C E", Font = new Font("Segoe UI", 7.3F), ForeColor = NasTheme.Muted, AutoSize = true, Location = new Point(59, 48) };
            brand.Controls.AddRange(new Control[] { logo, brandTitle, brandSubtitle });
            Panel connectionPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 24, 0, 21) };
            connectionBadge = new Label { Text = "●  연결 준비", AutoSize = false, Dock = DockStyle.Right, Width = 115, TextAlign = ContentAlignment.MiddleCenter, BackColor = NasTheme.Surface, ForeColor = NasTheme.Muted, Font = new Font("Malgun Gothic", 9F, FontStyle.Bold) };
            connectionPanel.Controls.Add(connectionBadge);
            header.Controls.Add(brand, 0, 0);
            header.Controls.Add(connectionPanel, 1, 0);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ShellButton home = CreateButton("NAS 홈", ShellGlyph.Home, false, delegate { NavigateHome(); });
            home.Compact = true;
            ShellButton refresh = CreateButton("새로 고침", ShellGlyph.Refresh, false, delegate { ReloadPage(); });
            refresh.Compact = true;
            Address = new AddressBar { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            Address.SetAddress(settings.Url);
            Address.ConnectRequested += delegate { ApplyAddress(); };
            if (popup) Address.EditSwitch.Enabled = false;
            toolTips.SetToolTip(home, "NAS 홈");
            toolTips.SetToolTip(refresh, "새로 고침");
            actions.Controls.Add(home, 0, 0);
            actions.Controls.Add(refresh, 1, 0);
            actions.Controls.Add(Address, 2, 0);
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
            retry.Click += async delegate { await ReconnectAsync(); };
            errorPanel.Controls.Add(retry);
            errorPanel.Controls.Add(errorText);
            errorPanel.Controls.Add(errorTitle);
            viewport = new Panel { Dock = DockStyle.Fill, BackColor = NasTheme.Border, Padding = new Padding(1) };
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

        private static ShellButton CreateButton(string text, ShellGlyph glyph, bool primary, Action action)
        {
            ShellButton button = new ShellButton { Text = text, AccessibleName = text, Glyph = glyph, Primary = primary, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
            button.Click += delegate { action(); };
            return button;
        }

        private void SetConnectionState(string text, Color color)
        {
            connectionBadge.Text = "●  " + text;
            connectionBadge.ForeColor = color;
        }

        private static WebView2 CreateBrowser()
        {
            return new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(24, 27, 32) };
        }

        internal Task InitializeAsync(CoreWebView2Environment sharedEnvironment)
        {
            if (Ready || IsDisposed || Disposing || exitRequested) return Task.FromResult(0);
            if (initializationTask != null && !initializationTask.IsCompleted) return initializationTask;
            initializationTask = InitializeBrowserAsync(sharedEnvironment);
            return initializationTask;
        }

        private async Task InitializeBrowserAsync(CoreWebView2Environment sharedEnvironment)
        {
            if (Ready || IsDisposed || Disposing) return;
            try
            {
                if (waitForBrowserExit && browserExitSignal != null)
                {
                    Task exited = browserExitSignal.Task;
                    if (await Task.WhenAny(exited, Task.Delay(3000)) != exited)
                    {
                        ShowError("웹 엔진이 아직 종료 중입니다. 잠시 후 다시 연결을 눌러 주세요.");
                        return;
                    }
                    waitForBrowserExit = false;
                }
                if (IsDisposed || Disposing || exitRequested) return;
                if (browserNeedsRecreation)
                {
                    foreach (MainWindow child in children.ToArray()) child.RequestExit();
                    viewport.Controls.Remove(Browser);
                    Browser.Dispose();
                    Browser = CreateBrowser();
                    viewport.Controls.Add(Browser);
                    errorPanel.BringToFront();
                    environment = null;
                    browserNeedsRecreation = false;
                }
                CoreWebView2Environment.GetAvailableBrowserVersionString();
                environment = sharedEnvironment ?? environment ?? await CoreWebView2Environment.CreateAsync(null, profilePath);
                TaskCompletionSource<bool> exitedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                browserExitSignal = exitedSignal;
                environment.BrowserProcessExited += delegate { exitedSignal.TrySetResult(true); };
                if (IsDisposed || Disposing) return;
                await Browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing) return;
                CoreWebView2 core = Browser.CoreWebView2;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (!connection.Allows(e.Uri))
                    {
                        e.Cancel = true;
                        if (connection.CanVerify(e.Uri)) QueueQuickConnectNavigation(e.Uri);
                        else if (!e.IsUserInitiated) ShowError("설정한 NAS와 관련 없는 주소로 이동하려 해 연결을 중단했습니다. QuickConnect ID 또는 NAS 주소를 확인해 주세요.");
                        else status.Text = "설정한 NAS와 관련 없는 주소로의 이동을 차단했습니다.";
                        return;
                    }
                    InvalidatePendingNavigation();
                    errorPanel.Visible = false;
                    status.Text = "NAS에 안전하게 연결하고 있습니다.";
                    SetConnectionState("연결 중", NasTheme.Muted);
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.IsSuccess)
                    {
                        if (!connection.Allows(core.Source)) return;
                        if (ConnectionSettings.IsValid(core.Source))
                            lastLoadedUrl = core.Source;
                        errorPanel.Visible = false;
                        if (connection.IsQuickConnect && core.Source != "about:blank" && !connection.IsNasOrigin(core.Source))
                        {
                            status.Text = "QuickConnect에서 NAS 연결 경로를 선택하고 있습니다.";
                            SetConnectionState("연결 중", NasTheme.Muted);
                            return;
                        }
                        status.Text = core.Source == "about:blank" ? "웹 화면 준비 완료" : (connection.IsQuickConnect ? "QuickConnect · " : "HTTPS · ") + new Uri(core.Source).Authority;
                        SetConnectionState("연결됨", NasTheme.Accent);
                    }
                    else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        ShowError("NAS에 연결하지 못했습니다: " + e.WebErrorStatus + "\n네트워크, NAS 전원, 접속 주소를 확인한 뒤 다시 연결하세요. 인증서 오류는 브라우저에서도 확인해 주세요.");
                };
                core.NewWindowRequested += OnNewWindowRequested;
                core.WindowCloseRequested += delegate { if (popup) RequestExit(); };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e)
                {
                    if (!connection.IsNasOrigin(e.Uri)) e.State = CoreWebView2PermissionState.Deny;
                };
                core.LaunchingExternalUriScheme += delegate(object sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e) { e.Cancel = true; };
                core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e) { HandleProcessFailure(e.ProcessFailedKind); };
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
                Ready = false;
                browserNeedsRecreation = true;
                InitializationError = error;
                ShowError("웹 화면을 준비하지 못했습니다. Microsoft Edge WebView2 Runtime 설치 상태를 확인하세요.\n\n" + error.Message);
            }
        }

        private void QueueQuickConnectNavigation(string target)
        {
            pendingNavigationUrl = target;
            navigationRequest++;
            if (navigationCheck == null || navigationCheck.IsCompleted)
                navigationCheck = VerifyQuickConnectNavigationAsync(connection);
        }

        private void InvalidatePendingNavigation()
        {
            pendingNavigationUrl = null;
            navigationRequest++;
        }

        private async Task VerifyQuickConnectNavigationAsync(ConnectionSession session)
        {
            while (!IsDisposed && !Disposing && !exitRequested && Object.ReferenceEquals(connection, session) && pendingNavigationUrl != null)
            {
                string target = pendingNavigationUrl;
                int request = navigationRequest;
                pendingNavigationUrl = null;
                status.Text = "QuickConnect가 안내한 NAS 연결 주소를 확인하고 있습니다.";
                SetConnectionState("경로 확인", NasTheme.Muted);
                try
                {
                    bool allowed = await session.VerifyAndAllowAsync(target);
                    if (IsDisposed || Disposing || exitRequested || !Object.ReferenceEquals(connection, session)) return;
                    if (request != navigationRequest) continue;
                    if (allowed && Ready) Browser.CoreWebView2.Navigate(target);
                    else if (Ready) ShowError("QuickConnect 연결 주소를 확인하지 못했습니다. NAS의 QuickConnect 설정과 네트워크 상태를 확인한 뒤 다시 연결해 주세요.");
                }
                catch (OperationCanceledException) { return; }
                catch (Exception)
                {
                    if (IsDisposed || Disposing || exitRequested || !Object.ReferenceEquals(connection, session)) return;
                    if (request != navigationRequest) continue;
                    ShowError("QuickConnect 연결 경로 확인에 실패했습니다. 네트워크와 NAS 상태를 확인한 뒤 다시 연결해 주세요. HTTPS 인증서 검증은 유지됩니다.");
                }
            }
        }

        internal void HandleProcessFailure(CoreWebView2ProcessFailedKind kind)
        {
            if (IsDisposed || Disposing || exitRequested) return;
            if (kind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                Ready = false;
                browserNeedsRecreation = true;
                waitForBrowserExit = true;
                ShowError("웹 엔진이 중단됐습니다. 다시 연결을 누르면 로그인 저장 공간을 유지하고 웹 화면을 새로 준비합니다.");
            }
            else if (kind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                ShowError("웹 화면이 중단됐습니다. 다시 연결을 누르면 NAS 화면을 다시 불러옵니다.");
            else if (kind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                status.Text = "웹 화면의 응답이 지연되고 있습니다. 기다리거나 새로 고침을 눌러 주세요.";
            else if (kind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
                status.Text = "일부 웹 화면이 중단됐습니다. 필요하면 새로 고침을 눌러 주세요.";
            // GPU, utility and other auxiliary processes are recovered by WebView2.
        }

        internal async Task ReconnectAsync()
        {
            if (IsDisposed || Disposing || exitRequested) return;
            if (!Ready) await InitializeAsync(null);
            else if (!popup) NavigateHome();
            if (Ready && popup)
            {
                string target = ConnectionSettings.IsValid(lastLoadedUrl) && connection.Allows(lastLoadedUrl)
                    ? lastLoadedUrl : settings.Url;
                errorPanel.Visible = false;
                Browser.CoreWebView2.Navigate(target);
            }
        }

        private async void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (exitRequested || IsDisposed || Disposing) return;
            ConnectionSession session = connection;
            CoreWebView2Deferral deferral = e.GetDeferral();
            MainWindow child = null;
            try
            {
                bool allowed = session.Allows(e.Uri) || await session.VerifyAndAllowAsync(e.Uri);
                if (exitRequested || IsDisposed || Disposing || !Object.ReferenceEquals(connection, session)) return;
                if (!allowed)
                {
                    if (e.IsUserInitiated) OpenExternal(e.Uri, true);
                    else status.Text = "다른 사이트의 자동 팝업을 차단했습니다.";
                    return;
                }
                child = new MainWindow(settings, profilePath, testMode, true, settingsPath, session);
                // Force HWND creation without displaying an uninitialized window.
                IntPtr handle = child.Handle;
                await child.InitializeAsync(environment);
                if (IsDisposed || exitRequested || !child.Ready || !Object.ReferenceEquals(connection, session)) { child.Dispose(); return; }
                e.NewWindow = child.Browser.CoreWebView2;
                children.Add(child);
                MainWindow trackedChild = child;
                child.FormClosed += delegate { children.Remove(trackedChild); hiddenChildren.Remove(trackedChild); };
                if (Visible) child.Show();
                else hiddenChildren.Add(child);
            }
            catch (OperationCanceledException) { if (child != null) child.Dispose(); }
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
            status.Text = "연결 확인 필요 · 주소 수정 / 다시 연결";
            SetConnectionState("연결 확인", Color.FromArgb(244, 184, 96));
        }

        private void NavigateHome()
        {
            if (!Ready) return;
            InvalidatePendingNavigation();
            errorPanel.Visible = false;
            Browser.CoreWebView2.Navigate(settings.Url);
        }

        private void ReloadPage()
        {
            if (!Ready) return;
            InvalidatePendingNavigation();
            if (errorPanel.Visible) NavigateHome();
            else Browser.Reload();
        }

        private void ApplyAddress()
        {
            string value = Address.AddressText.Trim();
            ConnectionAddress parsed;
            if (!ConnectionAddress.TryParse(value, out parsed))
            {
                status.Text = "QuickConnect ID·주소 또는 HTTPS NAS 주소를 입력하세요. 주소에 아이디나 비밀번호를 넣지 마세요.";
                Address.Input.Focus();
                return;
            }
            string address = parsed.Url;
            try
            {
                new ConnectionSettings { Url = address }.Save(settingsPath);
                foreach (MainWindow child in children.ToArray()) child.RequestExit();
                connection.Dispose();
                connection = new ConnectionSession(address);
                pendingNavigationUrl = null;
                navigationCheck = null;
                navigationRequest++;
                lastLoadedUrl = null;
                settings.Url = address;
                Address.SetAddress(address);
                NavigateHome();
            }
            catch (Exception error) { status.Text = "주소를 저장하지 못했습니다: " + error.Message; }
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
                if (!popup) connection.Dispose();
                foreach (MainWindow child in children.ToArray()) child.Dispose();
                if (tray != null)
                {
                    tray.Visible = false;
                    if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                    tray.Dispose();
                }
                Browser.Dispose();
                toolTips.Dispose();
                appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
