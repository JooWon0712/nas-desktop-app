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
        private readonly ConnectionStatusBadge connectionBadge;
        private readonly ContextMenuStrip themeMenu;
        private readonly List<ToolStripMenuItem> paletteItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> modeItems = new List<ToolStripMenuItem>();
        private ThemePalette appliedPalette;
        internal readonly AddressBar Address;
        private readonly string settingsPath;
        private readonly AddressHistoryStore historyStore;
        private string historyWarning;
        private readonly ConnectionFailureView errorPanel;
        private bool failureShown;
        private ulong activeNavigationId;
        private readonly Panel viewport;
        private Icon appIcon;
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
        internal bool IsFailureVisible { get { return failureShown; } }
        internal ConnectionFailureView FailureView { get { return errorPanel; } }

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
            historyStore = new AddressHistoryStore(testMode ? settingsPath + ".history.json" : Program.HistoryPath);
            appliedPalette = NasTheme.Current;
            connection = session ?? new ConnectionSession(settings.Url, settings.Kind);
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
            Label brandTitle = new Label { Text = Program.AppName, Font = new Font("Segoe UI", 17F, FontStyle.Bold), ForeColor = NasTheme.Text, AutoSize = true, Location = new Point(56, 13) };
            Label brandSubtitle = new Label { Text = "D E S K T O P   W O R K S P A C E", Font = new Font("Segoe UI", 7.3F), ForeColor = NasTheme.Muted, AutoSize = true, Location = new Point(59, 48) };
            brand.Controls.AddRange(new Control[] { logo, brandTitle, brandSubtitle });
            Panel connectionPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 20, 0, 20) };
            TableLayoutPanel appearance = new TableLayoutPanel { Dock = DockStyle.Right, Width = 208, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
            appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            appearance.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            connectionBadge = new ConnectionStatusBadge { Dock = DockStyle.Fill, Margin = Padding.Empty, Font = new Font("Malgun Gothic", 9F) };
            themeMenu = new ContextMenuStrip { Font = new Font("Malgun Gothic", 9F) };
            AddModeItems(themeMenu.Items);
            themeMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem colors = new ToolStripMenuItem("색상 테마");
            AddPaletteItems(colors.DropDownItems);
            themeMenu.Items.Add(colors);
            TrayMenuStyle.Configure(themeMenu);
            ShellButton themeButton = new ShellButton { Dock = DockStyle.Fill, Margin = Padding.Empty, Text = "테마", Glyph = ShellGlyph.Settings, AccessibleName = "색상 테마 선택" };
            themeButton.Click += delegate { themeMenu.Show(themeButton, new Point(themeButton.Width - themeMenu.GetPreferredSize(Size.Empty).Width, themeButton.Height + 6)); };
            appearance.Controls.Add(connectionBadge, 0, 0);
            appearance.Controls.Add(themeButton, 2, 0);
            connectionPanel.Controls.Add(appearance);
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
            Address.SetAddress(settings.Url, settings.Kind);
            Address.ConnectRequested += delegate { ApplyAddress(); };
            Address.HistoryRequested += delegate { ShowAddressHistory(); };
            if (popup) Address.EditingEnabled = false;
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

            errorPanel = new ConnectionFailureView { Dock = DockStyle.Fill, Visible = false };
            errorPanel.RetryRequested += async delegate { await ReconnectAsync(); };
            errorPanel.EditAddressButton.Enabled = !popup;
            errorPanel.EditAddressRequested += delegate
            {
                if (!Address.EditingEnabled) return;
                Address.IsEditing = true;
                status.Text = "접속 주소와 포트를 수정한 뒤 Enter 또는 연결을 눌러 주세요.";
            };
            viewport = new Panel { Dock = DockStyle.Fill, BackColor = NasTheme.Border, Padding = new Padding(1) };
            Panel workspace = new Panel { Dock = DockStyle.Fill, BackColor = NasTheme.Background, Padding = new Padding(20, 0, 20, 0) };
            viewport.Controls.Add(Browser);
            viewport.Controls.Add(errorPanel);
            workspace.Controls.Add(viewport);
            Controls.Add(workspace);
            Controls.Add(top);
            Controls.Add(footer);
            NasTheme.ApplyWindowChrome(this);

            if (!popup)
            {
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.BackColor = NasTheme.Surface;
                menu.ForeColor = NasTheme.Text;
                menu.Font = new Font("Malgun Gothic", 9F);
                menu.Items.Add("NAS 창 열기", null, delegate { RestoreFromTray(); });
                menu.Items.Add("새로 고침", null, delegate { ReloadPage(); });
                menu.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem modes = new ToolStripMenuItem("화면 모드");
                AddModeItems(modes.DropDownItems);
                menu.Items.Add(modes);
                ToolStripMenuItem themes = new ToolStripMenuItem("색상 테마");
                AddPaletteItems(themes.DropDownItems);
                menu.Items.Add(themes);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("완전 종료", null, delegate { RequestExit(); });
                TrayMenuStyle.Configure(menu);
                tray = new NotifyIcon { Text = Program.AppName, Icon = appIcon, ContextMenuStrip = menu, Visible = true };
                tray.DoubleClick += delegate { RestoreFromTray(); };
                tray.BalloonTipClicked += delegate { RestoreFromTray(); };
                Shown += async delegate { await InitializeAsync(null); };
            }
            NasTheme.Changed += OnThemeChanged;
            ResumeLayout(true);
        }

        private void AddPaletteItems(ToolStripItemCollection items)
        {
            foreach (ThemePalette palette in NasTheme.Palettes)
            {
                string key = palette.Key;
                ToolStripMenuItem item = new ToolStripMenuItem(palette.Name) { Tag = key, Checked = key == NasTheme.Current.Key };
                item.Click += delegate { SelectPalette(key); };
                paletteItems.Add(item);
                items.Add(item);
            }
        }

        private void AddModeItems(ToolStripItemCollection items)
        {
            AddModeItem(items, "시스템 설정 따르기", ThemeMode.System);
            AddModeItem(items, "라이트 모드", ThemeMode.Light);
            AddModeItem(items, "다크 모드", ThemeMode.Dark);
        }

        private void AddModeItem(ToolStripItemCollection items, string text, ThemeMode mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { Tag = mode, Checked = mode == NasTheme.Mode };
            item.Click += delegate { SelectMode(mode); };
            modeItems.Add(item);
            items.Add(item);
        }

        internal void SelectPalette(string key)
        {
            try
            {
                AppearanceSettings.Save(testMode ? settingsPath + ".appearance.json" : Program.AppearancePath, key, NasTheme.Mode);
                NasTheme.SetPalette(key);
            }
            catch (Exception error) { status.Text = "테마를 저장하지 못했습니다: " + error.Message; }
        }

        internal void SelectMode(ThemeMode mode)
        {
            try
            {
                AppearanceSettings.Save(testMode ? settingsPath + ".appearance.json" : Program.AppearancePath, NasTheme.Current.Key, mode);
                NasTheme.SetMode(mode);
            }
            catch (Exception error) { status.Text = "화면 모드를 저장하지 못했습니다: " + error.Message; }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            RecolorControls(this, appliedPalette);
            appliedPalette = NasTheme.Current;
            Address.RefreshTheme();
            errorPanel.RefreshTheme();
            foreach (ToolStripMenuItem item in paletteItems)
                item.Checked = (string)item.Tag == NasTheme.Current.Key;
            foreach (ToolStripMenuItem item in modeItems)
                item.Checked = (ThemeMode)item.Tag == NasTheme.Mode;
            if (!Browser.IsDisposed && !browserNeedsRecreation)
            {
                // A browser process can exit between the check and its native call.
                try { Browser.DefaultBackgroundColor = NasTheme.Surface; }
                catch (InvalidOperationException) { }
                catch (System.Runtime.InteropServices.COMException) { }
            }
            NasTheme.RefreshWindowChrome(this);
            Icon previous = appIcon;
            appIcon = NasTheme.CreateIcon();
            Icon = appIcon;
            if (tray != null) tray.Icon = appIcon;
            previous.Dispose();
            Invalidate(true);
        }

        internal static void RecolorControls(Control control, ThemePalette previous)
        {
            // Web content owns its colors and keeps the current document/session.
            if (control is WebView2) return;
            control.BackColor = Recolor(control.BackColor, previous);
            control.ForeColor = Recolor(control.ForeColor, previous);
            ToolStrip strip = control as ToolStrip;
            if (strip != null)
                foreach (ToolStripItem item in strip.Items) item.ForeColor = NasTheme.Muted;
            foreach (Control child in control.Controls) RecolorControls(child, previous);
            control.Invalidate();
        }

        private static Color Recolor(Color color, ThemePalette previous)
        {
            if (color == previous.Background) return NasTheme.Background;
            if (color == previous.Surface) return NasTheme.Surface;
            if (color == previous.Border) return NasTheme.Border;
            if (color == previous.Text) return NasTheme.Text;
            if (color == previous.Muted) return NasTheme.Muted;
            return color;
        }

        private static ShellButton CreateButton(string text, ShellGlyph glyph, bool primary, Action action)
        {
            ShellButton button = new ShellButton { Text = text, AccessibleName = text, Glyph = glyph, Primary = primary, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
            button.Click += delegate { action(); };
            return button;
        }

        private void SetConnectionState(string text, Color color)
        {
            connectionBadge.SetStatus(text, color);
        }

        private WebView2 CreateBrowser()
        {
            WebView2 browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = NasTheme.Surface };
            browser.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyData != (Keys.Control | Keys.L) || popup || exitRequested || IsDisposed) return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                // WinForms forwards native WebView accelerator keys here. Queue
                // focus changes until the synchronous browser callback returns.
                try { BeginInvoke((Action)OpenAddressEditor); }
                catch (InvalidOperationException) { }
            };
            return browser;
        }

        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            base.WndProc(ref message);
            if (kind == 0x001A || kind == 0x031A || kind == 0x0015)
            {
                // Windows broadcasts preference changes even to the hidden tray window.
                NasTheme.RefreshSystemAppearance();
                NasTheme.RefreshWindowChrome(this);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            NasTheme.RefreshSystemAppearance();
        }

        internal void OpenAddressEditor()
        {
            if (popup || !Address.EditingEnabled || IsDisposed || Disposing || exitRequested) return;
            Address.IsEditing = true;
        }

        internal bool UseHistoryAddress(ConnectionSettings candidate)
        {
            if (popup || !Address.EditingEnabled || candidate == null || IsDisposed || Disposing || exitRequested) return false;
            ConnectionSettings normalized;
            try
            {
                if (!ConnectionSettings.TryCreate(candidate.Url, candidate.Kind, out normalized)) return false;
            }
            catch (System.IO.InvalidDataException) { return false; }
            Address.IsEditing = true;
            Address.Kind = normalized.Kind;
            Address.AddressText = normalized.Url;
            Address.Input.Focus();
            Address.Input.SelectAll();
            status.Text = "기록의 주소를 입력했습니다. Enter 또는 연결을 누르면 접속합니다.";
            return true;
        }

        private void ShowAddressHistory()
        {
            if (popup || !Address.EditingEnabled || exitRequested) return;
            ConnectionSettings initial = settings;
            ConnectionSettings draft;
            if (Address.IsEditing && ConnectionSettings.TryCreate(Address.AddressText, Address.Kind, out draft)) initial = draft;
            using (AddressHistoryDialog dialog = new AddressHistoryDialog(historyStore, initial))
                if (dialog.ShowDialog(this) == DialogResult.OK) UseHistoryAddress(dialog.SelectedConnection);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.L) && !popup && Address.EditingEnabled)
            {
                OpenAddressEditor();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
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
                    Browser.Visible = !failureShown;
                    viewport.Controls.Add(Browser);
                    errorPanel.BringToFront();
                    environment = null;
                    browserNeedsRecreation = false;
                    activeNavigationId = 0;
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
                WebView2 initializedBrowser = Browser;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsBuiltInErrorPageEnabled = false;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(BrowserScrollbarStyle.Script);
                if (IsDisposed || Disposing || !Object.ReferenceEquals(Browser, initializedBrowser)) return;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (IsDisposed || exitRequested || !Object.ReferenceEquals(Browser, initializedBrowser)) { e.Cancel = true; return; }
                    if (!connection.Allows(e.Uri))
                    {
                        e.Cancel = true;
                        if (connection.CanVerify(e.Uri)) QueueQuickConnectNavigation(e.Uri);
                        else if (!e.IsUserInitiated) ShowError(connection.IsQuickConnect
                            ? "설정한 NAS와 관련 없는 주소로 이동하려 해 연결을 중단했습니다. QuickConnect ID 또는 NAS 주소를 확인해 주세요."
                            : "다른 호스트 또는 포트로의 자동 이동을 중단했습니다. NAS의 최종 HTTPS 관리 주소를 입력해 주세요.");
                        else status.Text = "설정한 NAS와 관련 없는 주소로의 이동을 차단했습니다.";
                        return;
                    }
                    activeNavigationId = e.NavigationId;
                    InvalidatePendingNavigation();
                    if (failureShown) errorPanel.SetBusy(true);
                    status.Text = "NAS에 안전하게 연결하고 있습니다.";
                    SetConnectionState("연결 중", NasTheme.Muted);
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (IsDisposed || exitRequested || !Ready || !Object.ReferenceEquals(Browser, initializedBrowser)
                        || e.NavigationId != activeNavigationId) return;
                    if (!e.IsSuccess && e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                    {
                        if (failureShown) errorPanel.SetBusy(false);
                        return;
                    }
                    ConnectionFailureInfo httpFailure = ConnectionFailureInfo.FromHttpStatus(e.HttpStatusCode);
                    if (httpFailure != null) { ShowError(httpFailure); return; }
                    if (e.IsSuccess)
                    {
                        if (!connection.Allows(core.Source)) return;
                        if (ConnectionSettings.IsValid(core.Source))
                            lastLoadedUrl = core.Source;
                        ShowBrowser();
                        if (connection.IsQuickConnect && core.Source != "about:blank" && !connection.IsNasOrigin(core.Source))
                        {
                            status.Text = "QuickConnect에서 NAS 연결 경로를 선택하고 있습니다.";
                            SetConnectionState("연결 중", NasTheme.Muted);
                            return;
                        }
                        status.Text = (core.Source == "about:blank" ? "웹 화면 준비 완료" : (connection.IsQuickConnect ? "QuickConnect 보안 연결" : "HTTPS 보안 연결")) + historyWarning;
                        SetConnectionState("연결됨", NasTheme.Accent);
                    }
                    else
                    {
                        ConnectionFailureInfo failure = ConnectionFailureInfo.FromWebError(e.WebErrorStatus, e.HttpStatusCode);
                        if (failure != null) ShowError(failure);
                        else
                        {
                            ShowBrowser();
                            status.Text = "NAS의 로그인 또는 권한 안내를 확인해 주세요.";
                            SetConnectionState("연결 확인", NasTheme.Warning);
                        }
                    }
                };
                core.NewWindowRequested += OnNewWindowRequested;
                core.WindowCloseRequested += delegate { if (popup) RequestExit(); };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e)
                {
                    if (!connection.IsNasOrigin(e.Uri)) e.State = CoreWebView2PermissionState.Deny;
                };
                core.LaunchingExternalUriScheme += delegate(object sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e) { e.Cancel = true; };
                core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e)
                {
                    if (Object.ReferenceEquals(Browser, initializedBrowser)) HandleProcessFailure(e.ProcessFailedKind);
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
            if (failureShown) errorPanel.SetBusy(true);
            try
            {
                if (!Ready) await InitializeAsync(null);
                else if (!popup) NavigateHome();
                if (Ready && popup)
                {
                    string target = ConnectionSettings.IsValid(lastLoadedUrl) && connection.Allows(lastLoadedUrl)
                        ? lastLoadedUrl : settings.Url;
                    Browser.CoreWebView2.Navigate(target);
                }
            }
            catch (InvalidOperationException) { ShowError("웹 화면을 다시 준비하지 못했습니다. 잠시 후 다시 연결해 주세요."); }
            catch (System.Runtime.InteropServices.COMException) { ShowError("웹 화면을 다시 준비하지 못했습니다. 잠시 후 다시 연결해 주세요."); }
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
            ShowFailure("연결을 확인해 주세요", message, null);
        }

        private void ShowError(ConnectionFailureInfo failure)
        {
            ShowFailure(failure.Title, failure.Description, failure.Detail);
        }

        private void ShowFailure(string title, string description, string detail)
        {
            if (IsDisposed || Disposing || exitRequested) return;
            failureShown = true;
            errorPanel.SetError(title, description, detail);
            SetBrowserVisibility(false);
            errorPanel.Visible = true;
            errorPanel.BringToFront();
            status.Text = "연결 확인 필요 · 주소 수정 / 다시 연결";
            SetConnectionState("연결 확인", NasTheme.Warning);
        }

        private void ShowBrowser()
        {
            failureShown = false;
            errorPanel.SetBusy(false);
            errorPanel.Visible = false;
            SetBrowserVisibility(true);
        }

        private void SetBrowserVisibility(bool visible)
        {
            if (Browser.IsDisposed) return;
            try
            {
                Browser.Visible = visible;
                if (visible) Browser.BringToFront();
            }
            catch (InvalidOperationException) { }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private void NavigateHome()
        {
            if (!Ready) return;
            InvalidatePendingNavigation();
            Browser.CoreWebView2.Navigate(settings.Url);
        }

        private void ReloadPage()
        {
            if (!Ready) return;
            InvalidatePendingNavigation();
            if (failureShown) NavigateHome();
            else Browser.Reload();
        }

        private async void ApplyAddress()
        {
            string value = Address.AddressText.Trim();
            ConnectionSettings candidate;
            if (!ConnectionSettings.TryCreate(value, Address.Kind, out candidate))
            {
                status.Text = Address.Kind == ConnectionKind.SynologyQuickConnect
                    ? "Synology QuickConnect ID 또는 quickconnect.to/ID 주소를 입력하세요."
                    : "NAS의 HTTPS 관리 주소를 입력하세요. 주소에 아이디나 비밀번호를 넣지 마세요.";
                Address.Input.Focus();
                return;
            }
            string address = candidate.Url;
            try
            {
                candidate.Save(settingsPath);
                foreach (MainWindow child in children.ToArray()) child.RequestExit();
                connection.Dispose();
                connection = new ConnectionSession(address, candidate.Kind);
                pendingNavigationUrl = null;
                navigationCheck = null;
                navigationRequest++;
                lastLoadedUrl = null;
                activeNavigationId = 0;
                settings.Url = address;
                settings.Type = candidate.Type;
                historyWarning = null;
                status.ToolTipText = String.Empty;
                try { historyStore.Add(candidate); }
                catch (Exception historyError)
                {
                    historyWarning = " · 주소 기록 저장 실패";
                    status.ToolTipText = "주소 기록을 저장하지 못했습니다: " + historyError.Message;
                }
                Address.SetAddress(address, candidate.Kind);
                if (Ready) NavigateHome();
                else await ReconnectAsync();
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
                NasTheme.Changed -= OnThemeChanged;
                themeMenu.Dispose();
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
