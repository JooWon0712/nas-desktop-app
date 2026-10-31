using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class Tests
    {
        private static readonly List<string> results = new List<string>();
        private static string output;
        private static string fixture;
        private static string uiSettingsPath;
        private static int exitCode = 1;

        [STAThread]
        private static int Main(string[] args)
        {
            output = args[0]; fixture = args[1];
            Directory.CreateDirectory(output);
            uiSettingsPath = Path.Combine(output, "ui-connection.json");
            ConnectionSettings initialSettings = new ConnectionSettings { Url = "https://nas-desk.test/" };
            initialSettings.Save(uiSettingsPath);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UnitTests();
            using (MainWindow window = new MainWindow(initialSettings, Path.Combine(output, "profile"), true, uiSettingsPath))
            {
                window.Shown += async delegate
                {
                    try
                    {
                        await RunBrowserTests(window);
                        exitCode = 0;
                    }
                    catch (Exception error) { results.Add("FAIL " + error); }
                    finally
                    {
                        window.RequestExit();
                        File.WriteAllLines(Path.Combine(output, "results.txt"), results);
                    }
                };
                Application.Run(window);
            }
            foreach (string result in results) Console.WriteLine(result);
            return exitCode;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            results.Add("PASS " + name);
        }

        private static void UnitTests()
        {
            ConnectionAddressTests.Run(Check);
            ConnectionSettingsTests.Run(output, Check);
            QuickConnectResolverTests.Run(Check);
            AppearanceSettingsTests.Run(output, Check);
            ThemeModeTests.Run(Check);
            ConnectionFailureInfoTests.Run(Check);
            AddressHistoryStoreTests.Run(output, Check);
            AddressHistoryDialogTests.Run(output, Check);
            Check(ConnectionSettings.IsValid("https://nas.example:5001/"), "HTTPS address accepted");
            Check(!ConnectionSettings.IsValid("http://nas.example/") && !ConnectionSettings.IsValid("https://user:secret@nas.example/"), "HTTP and credentials in URL rejected");
            Check(ConnectionSettings.IsInternal("https://nas.example:5001/webman/", "https://nas.example:5001/"), "DSM navigation accepted");
            Check(!ConnectionSettings.IsInternal("https://nas.example.attacker.invalid:5001/", "https://nas.example:5001/"), "Lookalike host rejected");
            Check(!ConnectionSettings.IsInternal("https://nas.example:5002/", "https://nas.example:5001/"), "Different origin requires external navigation");
            Check(MainWindow.ShouldHide(CloseReason.UserClosing, false, true, false), "X hides primary window");
            Check(!MainWindow.ShouldHide(CloseReason.WindowsShutDown, false, true, false)
                && !MainWindow.ShouldHide(CloseReason.TaskManagerClosing, false, true, false)
                && !MainWindow.ShouldHide(CloseReason.UserClosing, true, true, false)
                && !MainWindow.ShouldHide(CloseReason.UserClosing, false, true, true)
                && !MainWindow.ShouldHide(CloseReason.UserClosing, false, false, false), "OS shutdown, task manager, explicit exit, popups and missing tray do not hide");
            string settingsFile = Path.Combine(output, "connection.json");
            new ConnectionSettings { Url = "https://nas.example:5001/" }.Save(settingsFile);
            new ConnectionSettings { Url = "https://nas.example:5002/" }.Save(settingsFile);
            Check(ConnectionSettings.Read(settingsFile).Url == "https://nas.example:5002/", "Connection settings saved and replaced atomically");
        }

        private static async Task WaitFor(Func<bool> condition, int milliseconds, string description)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition())
            {
                if (DateTime.UtcNow > end) throw new TimeoutException(description);
                await Task.Delay(100);
            }
        }

        private static async Task RunBrowserTests(MainWindow window)
        {
            await ConnectionSessionTests.RunAsync(Check);
            await WaitFor(() => window.Ready || window.InitializationError != null, 20000, "WebView2 initialization");
            if (window.InitializationError != null) throw window.InitializationError;
            Check(window.Ready && window.TrayVisible, "WebView2 and notification icon initialized");
            CoreWebView2 core = window.Browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("nas-desk.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Navigate("https://nas-desk.test/index.html");
            await WaitFor(() => core.DocumentTitle == "NAS Desk local fixture", 10000, "Local HTTPS fixture");
            Check(!core.Settings.AreHostObjectsAllowed && !core.Settings.IsWebMessageEnabled, "Native host bridge disabled");
            Check((await core.ExecuteScriptAsync("document.cookie")).Contains("nas_desk_test=persisted"), "HTTPS cookies work");
            await RunThemeTests(window, core);
            await core.ExecuteScriptAsync("window.open('about:blank', 'nas_desk_popup')");
            await WaitFor(() => Application.OpenForms.Count >= 2, 10000, "Same-profile popup");
            MainWindow popup = Application.OpenForms.Cast<Form>().OfType<MainWindow>().First(w => w != window);
            await WaitFor(() => popup.Ready, 10000, "Popup document");
            Check((await popup.Browser.CoreWebView2.ExecuteScriptAsync("document.cookie")).Contains("nas_desk_test=persisted"), "Popup shares NAS session");
            popup.Address.IsEditing = true;
            Check(!popup.Address.EditingEnabled && !popup.Address.IsEditing && popup.Address.Input.ReadOnly
                && !popup.Address.TypePicker.Enabled && !popup.Address.LockButton.Enabled,
                "NAS popups retain a read-only address despite automatic editing in the primary window");
            int before = Int32.Parse(await core.ExecuteScriptAsync("window.ticks"));
            window.Close();
            Check(!window.Visible && !window.IsDisposed && window.TrayVisible, "Real Close hides without disposing WebView2");
            Check(!popup.Visible && !popup.IsDisposed, "Popup also hides with primary window");
            await Task.Delay(2500);
            Check(Int32.Parse(await core.ExecuteScriptAsync("window.ticks")) > before, "Web scripts continue while hidden");
            window.RestoreFromTray();
            Check(window.Visible && popup.Visible && Object.ReferenceEquals(core, window.Browser.CoreWebView2), "Restore retains primary and popup WebView2 instances");
            Check((await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')")) == "\"persisted\"", "Web storage retained after hide/restore");
            await popup.Browser.CoreWebView2.ExecuteScriptAsync("window.close()");
            await WaitFor(() => popup.IsDisposed, 5000, "window.close closes popup");
            Check(popup.IsDisposed && !window.IsDisposed, "Web popup close leaves primary window running");
            bool blocked = false;
            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { if (e.Uri.StartsWith("https://blocked.invalid/")) blocked = e.Cancel; };
            core.Navigate("https://blocked.invalid/");
            await WaitFor(() => blocked, 5000, "External navigation blocked before request");
            Check(blocked, "Unconfigured origin blocked");
            await QuickConnectBrowserTests.RunAsync(output, fixture, Check);
            await GenericNasBrowserTests.RunAsync(output, fixture, Check);
            await ConnectionFailureBrowserTests.RunAsync(output, fixture, Check);
            await BrowserScrollbarTests.RunAsync(output, fixture, Check);
            await RunAddressTests(window, core);
            await AddressAutoEditTests.RunAsync(window, core, uiSettingsPath, Check);
            await AddressModalBrowserTests.RunAsync(window, core, uiSettingsPath, Check);
            await RunRecoveryTests(window, core);
            await RunDownloadTests(window.Browser.CoreWebView2);
            window.RequestExit();
            Check(window.IsDisposed && popup.IsDisposed && !window.TrayVisible, "Explicit exit disposes windows and tray");
        }

        private static async Task RunThemeTests(MainWindow window, CoreWebView2 core)
        {
            string originalTheme = NasTheme.Current.Key;
            ThemeMode originalMode = NasTheme.Mode;
            string source = core.Source;
            string committed = window.Address.AddressText;
            await core.ExecuteScriptAsync("window.themeChangeMarker='same-document';");
            window.Address.IsEditing = true;
            window.Address.AddressText = "https://unsaved.example.test/";
            window.SelectPalette("violet");
            window.SelectMode(ThemeMode.Light);
            Check(AppearanceSettings.Read(uiSettingsPath + ".appearance.json") == "violet"
                && NasTheme.Current.Key == "violet"
                && AppearanceSettings.ReadPreferences(uiSettingsPath + ".appearance.json").SelectedMode == ThemeMode.Light,
                "Color and light mode persist in isolated appearance settings");
            Check(Object.ReferenceEquals(core, window.Browser.CoreWebView2) && core.Source == source
                && await core.ExecuteScriptAsync("window.themeChangeMarker") == "\"same-document\""
                && (await core.ExecuteScriptAsync("document.cookie")).Contains("nas_desk_test=persisted"),
                "Theme change retains the current web document and login storage");
            Check(window.Address.IsEditing && window.Address.AddressText == "https://unsaved.example.test/"
                && ConnectionSettings.Read(uiSettingsPath).Url == committed,
                "Theme change preserves an unsaved address edit and saved NAS address");
            window.SelectMode(ThemeMode.System);
            bool targetDark = !NasTheme.IsDark;
            window.HideToTray();
            NasTheme.RefreshSystemAppearance(targetDark);
            Check(!window.Visible && NasTheme.IsDark == targetDark && window.BackColor == NasTheme.Background
                && window.Address.Input.BackColor == NasTheme.Surface
                && AppearanceSettings.ReadPreferences(uiSettingsPath + ".appearance.json").SelectedMode == ThemeMode.System
                && await core.ExecuteScriptAsync("window.themeChangeMarker") == "\"same-document\"",
                "System brightness changes update a hidden tray window without reloading its document or overwriting automatic mode");
            window.RestoreFromTray();
            window.Address.IsEditing = false;
            window.SelectPalette(originalTheme);
            window.SelectMode(originalMode);
        }

        private static async Task RunDownloadTests(CoreWebView2 core)
        {
            string destination = Path.Combine(output, "nas-download-fixture.txt");
            CoreWebView2DownloadOperation download = null;
            EventHandler<CoreWebView2DownloadStartingEventArgs> started = delegate(object sender, CoreWebView2DownloadStartingEventArgs e)
            {
                e.ResultFilePath = destination;
                e.Handled = false;
                download = e.DownloadOperation;
            };
            core.DownloadStarting += started;
            try
            {
                await core.ExecuteScriptAsync("var file=new Blob(['NAS Desk local download fixture'],{type:'text/plain'}); var link=document.createElement('a'); link.href=URL.createObjectURL(file); link.download='nas-download-fixture.txt'; link.click();");
                await WaitFor(() => download != null && download.State != CoreWebView2DownloadState.InProgress, 10000, "Local file download start/completion");
                Check(download.State == CoreWebView2DownloadState.Completed, "Local download completed (interrupt reason: " + download.InterruptReason + ")");
                Check(File.ReadAllText(destination) == "NAS Desk local download fixture", "File download completes without a toolbar download button");
                core.OpenDefaultDownloadDialog();
                Check(core.IsDefaultDownloadDialogOpen && core.Settings.AreBrowserAcceleratorKeysEnabled, "Default download list remains available with browser shortcuts enabled");
                core.CloseDefaultDownloadDialog();
            }
            finally { core.DownloadStarting -= started; }
        }

        private static async Task RunRecoveryTests(MainWindow window, CoreWebView2 core)
        {
            string profile = core.Environment.UserDataFolder;
            Check(Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar).Equals(
                Path.GetFullPath(Path.Combine(output, "profile")).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
                "Crash test targets only the isolated test profile");
            var originalBrowser = window.Browser;
            window.HandleProcessFailure(CoreWebView2ProcessFailedKind.GpuProcessExited);
            window.HandleProcessFailure(CoreWebView2ProcessFailedKind.UtilityProcessExited);
            Check(window.Ready && Object.ReferenceEquals(originalBrowser, window.Browser)
                && await core.ExecuteScriptAsync("21*2") == "42", "Auto-recoverable GPU and utility failures do not disable the app");

            bool rendererFailed = false;
            bool browserFailed = false;
            core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e)
            {
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited) rendererFailed = true;
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) browserFailed = true;
            };
            Task rendererCrash = core.CallDevToolsProtocolMethodAsync("Page.crash", "{}");
            ObserveExpectedCrash(rendererCrash);
            await WaitFor(() => rendererFailed, 10000, "Test-profile renderer process failure");
            await window.ReconnectAsync();
            await WaitFor(() => window.Ready && core.DocumentTitle == "NAS Desk local fixture", 10000, "Renderer retry reload");
            Check(Object.ReferenceEquals(originalBrowser, window.Browser), "Actual renderer crash reconnects using the existing control");

            Task browserCrash = core.CallDevToolsProtocolMethodAsync("Browser.crash", "{}");
            ObserveExpectedCrash(browserCrash);
            await WaitFor(() => browserFailed && !window.Ready, 10000, "Test-profile browser process failure");
            ThemeMode modeBeforeFailure = NasTheme.Mode;
            NasTheme.SetMode(ThemeMode.System);
            NasTheme.RefreshSystemAppearance(!NasTheme.IsDark);
            Check(!window.Ready && !window.IsDisposed && window.BackColor == NasTheme.Background,
                "System mode updates during a browser process failure do not interrupt the app");
            bool recreatedWhileHidden = false;
            Control viewport = window.Browser.Parent;
            ControlEventHandler observeRecreation = delegate(object sender, ControlEventArgs e)
            {
                if (Object.ReferenceEquals(e.Control, window.Browser) && !Object.ReferenceEquals(e.Control, originalBrowser))
                    recreatedWhileHidden = window.IsFailureVisible && !e.Control.Visible;
            };
            viewport.ControlAdded += observeRecreation;
            try { await window.ReconnectAsync(); }
            finally { viewport.ControlAdded -= observeRecreation; }
            Check(recreatedWhileHidden, "The replacement browser remains hidden while the failure screen prepares its engine");
            Check(window.Ready && originalBrowser.IsDisposed && !Object.ReferenceEquals(originalBrowser, window.Browser)
                && window.Browser.CoreWebView2.Environment.UserDataFolder == profile,
                "Actual browser crash recreates the control and retains its profile");
            Check(window.Browser.DefaultBackgroundColor == NasTheme.Surface,
                "Recreated browser uses the appearance selected while its old process was unavailable");
            NasTheme.SetMode(modeBeforeFailure);
            CoreWebView2 recovered = window.Browser.CoreWebView2;
            recovered.SetVirtualHostNameToFolderMapping("nas-desk-alternate.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            recovered.Navigate("https://nas-desk-alternate.test/index.html");
            await WaitFor(() => recovered.DocumentTitle == "NAS Desk local fixture", 10000, "NAS reload after browser recovery");
            Check(!recovered.Settings.AreHostObjectsAllowed && !recovered.Settings.IsWebMessageEnabled,
                "Recreated browser restores host isolation and loads the NAS page");
        }

        private static void ObserveExpectedCrash(Task operation)
        {
            operation.ContinueWith(t => { var observed = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static async Task RunAddressTests(MainWindow window, CoreWebView2 core)
        {
            const string originalUrl = "https://nas-desk.test/";
            AddressBar address = window.Address;
            AddressHistoryStore history = new AddressHistoryStore(uiSettingsPath + ".history.json");
            Func<string> historySnapshot = () => String.Join("\n", history.ReadEntries().Select(item => item.Type + "\t" + item.Url + "\t" + item.Name));
            System.Drawing.Size originalSize = window.Size;
            window.Size = window.MinimumSize;
            window.PerformLayout();
            Check(address.Input.Visible && address.TypePicker.Visible && address.Input.ReadOnly
                && !address.TypePicker.Enabled && !address.ConnectButton.Visible
                && address.Input.Parent.Right < address.LockButton.Left
                && address.Input.Bounds.Bottom <= address.Input.Parent.ClientSize.Height
                && address.Input.Width >= TextRenderer.MeasureText("https://", address.Input.Font).Width
                && address.LockButton.Visible && address.HistoryButton.Visible
                && address.LockButton.Left >= 0 && address.LockButton.Bottom <= address.ClientSize.Height
                && address.LockButton.Right < address.HistoryButton.Left
                && address.HistoryButton.Right == address.ClientSize.Width
                && address.HistoryButton.Bottom <= address.ClientSize.Height,
                "The always-visible locked address and right-aligned actions fit at minimum window size");
            address.LockButton.PerformClick();
            window.PerformLayout();
            Check(address.Input.Parent.Bottom <= address.ClientSize.Height
                && address.Input.Visible && address.TypePicker.Visible && address.ConnectButton.Visible
                && address.Input.Parent.Right < address.LockButton.Left
                && address.LockButton.Bottom <= address.ClientSize.Height
                && address.LockButton.Right < address.ConnectButton.Left
                && address.ConnectButton.Bottom <= address.ClientSize.Height
                && address.ConnectButton.Right < address.HistoryButton.Left
                && address.HistoryButton.Right <= address.ClientSize.Width
                && address.Input.Bounds.Bottom <= address.Input.Parent.ClientSize.Height
                && address.Bottom <= address.Parent.ClientSize.Height
                && address.Input.Width >= TextRenderer.MeasureText("https://", address.Input.Font).Width,
                "Visible address input, type, cancel, connection and history controls fit at minimum window size");
            Check(Math.Abs(address.Input.Top - (address.Input.Parent.ClientSize.Height - address.Input.Height - address.Input.Top)) <= 1,
                "Address text stays vertically centered at the application DPI");
            address.IsEditing = false;
            window.Size = originalSize;
            window.PerformLayout();
            string originalSettings = File.ReadAllText(uiSettingsPath);
            string originalPage = core.Source;
            string originalHistory = historySnapshot();
            Check(!address.IsEditing && address.Input.ReadOnly && !address.TypePicker.Enabled
                && address.Input.Visible && address.TypePicker.Visible && !address.ConnectButton.Visible
                && address.LockButton.Visible && address.HistoryButton.Visible
                && address.Kind == ConnectionKind.DirectHttps && !address.ConnectButton.Enabled && address.AddressText == originalUrl,
                "Saved connection keeps the address visible and read-only with change and history actions available");

            address.IsEditing = true;
            Check(address.Input.Visible && !address.Input.ReadOnly && address.TypePicker.Visible && address.TypePicker.Enabled
                && address.ConnectButton.Visible && address.ConnectButton.Enabled,
                "Explicit editing enables the visible address and type and reveals the connection action");
            address.AddressText = "https://uncommitted.test/";
            address.Kind = ConnectionKind.SynologyQuickConnect;
            address.IsEditing = false;
            Check(address.Input.ReadOnly && address.Input.Visible && !address.TypePicker.Enabled && address.Kind == ConnectionKind.DirectHttps
                && address.AddressText == originalUrl && File.ReadAllText(uiSettingsPath) == originalSettings && core.Source == originalPage
                && historySnapshot() == originalHistory,
                "Cancelling editing restores the visible address and type without saving, recording or navigating");

            address.IsEditing = true;
            address.AddressText = "https://cancelled.test/";
            address.Kind = ConnectionKind.SynologyQuickConnect;
            Message escape = Message.Create(address.Input.Handle, 0x0100, (IntPtr)Keys.Escape, IntPtr.Zero);
            bool escapeHandled = address.PreProcessMessage(ref escape);
            Check(escapeHandled && !address.IsEditing && address.Kind == ConnectionKind.DirectHttps
                && address.Input.Visible && address.TypePicker.Visible && !address.ConnectButton.Visible
                && address.AddressText == originalUrl && File.ReadAllText(uiSettingsPath) == originalSettings
                && core.Source == originalPage && historySnapshot() == originalHistory,
                "Local Escape command cancels both address and connection type");

            address.IsEditing = true;
            address.AddressText = "http://nas-desk-alternate.test/index.html";
            address.ConnectButton.PerformClick();
            Check(address.IsEditing && address.Input.Visible && File.ReadAllText(uiSettingsPath) == originalSettings
                && core.Source == originalPage && historySnapshot() == originalHistory,
                "HTTP address submission preserves saved address, history and current page");
            address.AddressText = "https://user:secret@nas-desk-alternate.test/index.html";
            address.ConnectButton.PerformClick();
            Check(address.IsEditing && address.Input.Visible && File.ReadAllText(uiSettingsPath) == originalSettings
                && core.Source == originalPage && historySnapshot() == originalHistory,
                "Credentials in address submission are rejected without changing settings, history or document");

            const string alternateUrl = "https://nas-desk-alternate.test/index.html";
            core.SetVirtualHostNameToFolderMapping("nas-desk-alternate.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            bool alternateLoaded = false;
            EventHandler<CoreWebView2NavigationCompletedEventArgs> navigated = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.IsSuccess && core.Source == alternateUrl) alternateLoaded = true;
            };
            core.NavigationCompleted += navigated;
            try
            {
                address.AddressText = alternateUrl;
                address.ConnectButton.PerformClick();
                await WaitFor(() => alternateLoaded, 10000, "Updated NAS address loads its new HTTPS origin");
                Check(ConnectionSettings.Read(uiSettingsPath).Url == alternateUrl && address.AddressText == alternateUrl
                    && ConnectionSettings.Read(uiSettingsPath).Kind == ConnectionKind.DirectHttps
                    && !address.IsEditing && address.Input.ReadOnly && !address.ConnectButton.Enabled
                    && address.Input.Visible && address.TypePicker.Visible && !address.TypePicker.Enabled && !address.ConnectButton.Visible
                    && history.Read().Any(item => item.Url == alternateUrl && item.Kind == ConnectionKind.DirectHttps)
                    && core.Source == alternateUrl && core.DocumentTitle == "NAS Desk local fixture",
                    "Valid address submission saves isolated settings and history, locks the visible address and loads the new HTTPS origin");
            }
            finally { core.NavigationCompleted -= navigated; }

            const string quickConnectUrl = "https://ui-type-fixture.quickconnect.to/index.html";
            core.SetVirtualHostNameToFolderMapping("ui-type-fixture.quickconnect.to", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            address.IsEditing = true;
            address.Kind = ConnectionKind.SynologyQuickConnect;
            address.AddressText = quickConnectUrl;
            address.ConnectButton.PerformClick();
            await WaitFor(() => core.Source == quickConnectUrl && core.DocumentTitle == "NAS Desk local fixture", 10000, "Explicit QuickConnect type fixture");
            Check(ConnectionSettings.Read(uiSettingsPath).Kind == ConnectionKind.SynologyQuickConnect
                && address.Kind == ConnectionKind.SynologyQuickConnect && !address.IsEditing && address.Input.Visible
                && history.Read().Any(item => item.Url == quickConnectUrl && item.Kind == ConnectionKind.SynologyQuickConnect),
                "Connection editor applies and saves an explicitly selected QuickConnect type");
            address.IsEditing = true;
            address.Kind = ConnectionKind.DirectHttps;
            address.AddressText = alternateUrl;
            address.ConnectButton.PerformClick();
            await WaitFor(() => core.Source == alternateUrl && core.DocumentTitle == "NAS Desk local fixture", 10000, "Return to direct HTTPS fixture");
            Check(ConnectionSettings.Read(uiSettingsPath).Kind == ConnectionKind.DirectHttps
                && address.Kind == ConnectionKind.DirectHttps,
                "Switching from QuickConnect back to a generic HTTPS NAS replaces the saved type");
        }
    }
}
