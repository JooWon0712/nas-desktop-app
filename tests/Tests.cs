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
            BrowserProfileIdentityTests.Run(Check);
            WebContentPolicyTests.Run(output, Check);
            AddressHistoryStoreTests.Run(output, Check);
            AddressHistoryDialogTests.Run(output, Check);
            WindowLifecycleTests.Run(output, Check);
            Check(ConnectionSettings.IsValid("https://nas.example:5001/"), "HTTPS address accepted");
            Check(!ConnectionSettings.IsValid("http://nas.example/") && !ConnectionSettings.IsValid("https://user:secret@nas.example/"), "HTTP and credentials in URL rejected");
            Check(ConnectionSettings.IsInternal("https://nas.example:5001/webman/", "https://nas.example:5001/"), "DSM navigation accepted");
            Check(!ConnectionSettings.IsInternal("https://nas.example.attacker.invalid:5001/", "https://nas.example:5001/"), "Lookalike host rejected");
            Check(!ConnectionSettings.IsInternal("https://nas.example:5002/", "https://nas.example:5001/"), "Different origin requires external navigation");
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
            Check(window.Ready && !window.IsDisposed && window.MinimizeBox && window.ControlBox,
                "WebView2 initializes in a standard window with minimize and close controls");
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
            Check(!popup.Address.HistoryEnabled && popup.Address.Input.ReadOnly && popup.Address.Input.Visible
                && !popup.Address.TypePicker.Enabled && !popup.Address.HistoryButton.Enabled,
                "NAS popups retain a visible read-only address without connection selection controls");
            int before = Int32.Parse(await core.ExecuteScriptAsync("window.ticks"));
            window.WindowState = FormWindowState.Minimized;
            Check(window.WindowState == FormWindowState.Minimized && !window.IsDisposed && !popup.IsDisposed
                && Object.ReferenceEquals(core, window.Browser.CoreWebView2),
                "Minimizing keeps the main WebView and its related popup alive");
            await Task.Delay(2500);
            Check(Int32.Parse(await core.ExecuteScriptAsync("window.ticks")) > before, "Web scripts continue while the main window is minimized");
            window.RestoreWindow();
            Check(window.Visible && window.WindowState == FormWindowState.Normal && !popup.IsDisposed
                && Object.ReferenceEquals(core, window.Browser.CoreWebView2),
                "Restoring the main window retains its existing WebView and related popup");
            Check((await core.ExecuteScriptAsync("document.cookie")).Contains("nas_desk_test=persisted")
                && (await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')")) == "\"persisted\"",
                "Cookies and web storage survive minimize and restore");
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
            await BrowserProfileIsolationTests.RunAsync(output, Check);
            await WebContentPolicyBrowserTests.RunAsync(output, Check);
            await RunAddressTests(window, core);
            core = window.Browser.CoreWebView2;
            await AddressReadOnlyTests.RunAsync(window, core, uiSettingsPath, Check);
            await AddressModalBrowserTests.RunAsync(window, core, uiSettingsPath, fixture, Check);
            core = window.Browser.CoreWebView2;
            await RunRecoveryTests(window, core);
            await RunDownloadTests(window.Browser.CoreWebView2);
            // Keep the real X/Close test last: it now ends the application's
            // main message loop rather than hiding a still-running window.
            CoreWebView2 finalCore = window.Browser.CoreWebView2;
            HashSet<Form> beforeClosePopup = new HashSet<Form>(Application.OpenForms.Cast<Form>());
            MainWindow closePopup = null;
            await finalCore.ExecuteScriptAsync("window.open('about:blank','main_close_fixture');");
            await WaitFor(() => {
                closePopup = Application.OpenForms.Cast<Form>().OfType<MainWindow>().FirstOrDefault(form => !beforeClosePopup.Contains(form));
                return closePopup != null && closePopup.Ready;
            }, 10000, "Related popup for the main-window close test");
            var finalBrowser = window.Browser;
            var finalPopupBrowser = closePopup.Browser;
            window.Close();
            Check(window.IsDisposed && popup.IsDisposed && closePopup.IsDisposed
                && finalBrowser.IsDisposed && finalPopupBrowser.IsDisposed,
                "Closing the main window through X semantics disposes the app and all related popup WebViews");
        }

        private static async Task RunThemeTests(MainWindow window, CoreWebView2 core)
        {
            string originalTheme = NasTheme.Current.Key;
            ThemeMode originalMode = NasTheme.Mode;
            string source = core.Source;
            string committed = window.Address.AddressText;
            ConnectionKind committedKind = window.Address.Kind;
            await core.ExecuteScriptAsync("window.themeChangeMarker='same-document';");
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
            Check(window.Address.Input.ReadOnly && !window.Address.TypePicker.Enabled
                && window.Address.AddressText == committed && window.Address.Kind == committedKind
                && ConnectionSettings.Read(uiSettingsPath).Url == committed,
                "Theme change preserves the read-only address, connection type and saved NAS settings");
            window.SelectMode(ThemeMode.System);
            bool targetDark = !NasTheme.IsDark;
            window.WindowState = FormWindowState.Minimized;
            NasTheme.RefreshSystemAppearance(targetDark);
            Check(window.WindowState == FormWindowState.Minimized && !window.IsDisposed
                && NasTheme.IsDark == targetDark && window.BackColor == NasTheme.Background
                && window.Address.Input.BackColor == NasTheme.Surface
                && AppearanceSettings.ReadPreferences(uiSettingsPath + ".appearance.json").SelectedMode == ThemeMode.System
                && await core.ExecuteScriptAsync("window.themeChangeMarker") == "\"same-document\"",
                "System brightness changes update a minimized window without reloading its document or overwriting automatic mode");
            window.RestoreWindow();
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
            Check(address.Input.Visible && address.TypePicker.Visible && address.Input.ReadOnly && !address.TypePicker.Enabled
                && address.HistoryButton.Visible && address.HistoryButton.Enabled
                && address.Input.Parent.Right < address.HistoryButton.Left
                && address.Input.Parent.Bottom <= address.ClientSize.Height
                && address.Input.Bounds.Bottom <= address.Input.Parent.ClientSize.Height
                && address.Input.Width >= TextRenderer.MeasureText("https://", address.Input.Font).Width
                && address.HistoryButton.Right <= address.ClientSize.Width
                && address.HistoryButton.Bottom <= address.ClientSize.Height,
                "The read-only address, type and history action fit at minimum window size");
            Check(Math.Abs(address.Input.Top - (address.Input.Parent.ClientSize.Height - address.Input.Height - address.Input.Top)) <= 1,
                "Read-only address text stays vertically centered at the application DPI");
            window.Size = originalSize;
            window.PerformLayout();
            string originalSettings = File.ReadAllText(uiSettingsPath);
            string originalPage = core.Source;
            string originalHistory = historySnapshot();
            Check(address.Input.ReadOnly && address.Input.Visible && address.TypePicker.Visible && !address.TypePicker.Enabled
                && address.HistoryEnabled && address.Kind == ConnectionKind.DirectHttps && address.AddressText == originalUrl,
                "The saved connection is always visible and read-only with history selection available");

            Check(!window.ApplyConnection(null) && File.ReadAllText(uiSettingsPath) == originalSettings
                && core.Source == originalPage && historySnapshot() == originalHistory && address.AddressText == originalUrl,
                "An absent connection selection cannot change the saved address, history or NAS document");
            Check(!window.ApplyConnection(new ConnectionSettings { Url = "http://nas-desk-alternate.test/index.html", Type = "https" })
                && File.ReadAllText(uiSettingsPath) == originalSettings && core.Source == originalPage
                && historySnapshot() == originalHistory && address.AddressText == originalUrl,
                "HTTP connection submission preserves the displayed address, saved settings, history and current page");
            Check(!window.ApplyConnection(new ConnectionSettings { Url = "https://user:secret@nas-desk-alternate.test/index.html", Type = "https" })
                && File.ReadAllText(uiSettingsPath) == originalSettings && core.Source == originalPage
                && historySnapshot() == originalHistory && address.AddressText == originalUrl,
                "Credentials in a selected connection are rejected without changing settings, history or document");
            Check(!window.ApplyConnection(new ConnectionSettings { Url = "https://nas-desk-alternate.test/index.html", Type = "invalid-type" })
                && File.ReadAllText(uiSettingsPath) == originalSettings && core.Source == originalPage
                && historySnapshot() == originalHistory && address.Kind == ConnectionKind.DirectHttps,
                "An invalid connection type cannot replace the saved NAS or its read-only address");

            const string alternateUrl = "https://nas-desk-alternate.test/index.html";
            core.SetVirtualHostNameToFolderMapping("nas-desk-alternate.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            ConnectionSettings alternate;
            ConnectionSettings.TryCreate(alternateUrl, ConnectionKind.DirectHttps, out alternate);
            history.Add(alternate, "Selected fixture NAS");
            string[] fixtureHosts = { "nas-desk.test", "nas-desk-alternate.test", "ui-type-fixture.quickconnect.to" };
            bool applied = false;
            core = await BrowserFixtureNavigation.RunAsync(window, fixture, fixtureHosts,
                delegate { applied = window.ApplyConnection(alternate); }, alternateUrl, "NAS Desk local fixture");
            Check(applied && ConnectionSettings.Read(uiSettingsPath).Url == alternateUrl && address.AddressText == alternateUrl
                && ConnectionSettings.Read(uiSettingsPath).Kind == ConnectionKind.DirectHttps
                && address.Input.ReadOnly && address.Input.Visible && address.TypePicker.Visible && !address.TypePicker.Enabled
                && history.ReadEntries().Any(item => item.Url == alternateUrl && item.Kind == ConnectionKind.DirectHttps
                    && item.Name == "Selected fixture NAS") && core.Source == alternateUrl,
                "An explicit valid connection saves settings, preserves its NAS name and updates the read-only address");

            const string quickConnectUrl = "https://ui-type-fixture.quickconnect.to/index.html";
            core.SetVirtualHostNameToFolderMapping("ui-type-fixture.quickconnect.to", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            ConnectionSettings quickConnect;
            ConnectionSettings.TryCreate(quickConnectUrl, ConnectionKind.SynologyQuickConnect, out quickConnect);
            bool quickConnectApplied = false;
            core = await BrowserFixtureNavigation.RunAsync(window, fixture, fixtureHosts,
                delegate { quickConnectApplied = window.ApplyConnection(quickConnect); }, quickConnectUrl, "NAS Desk local fixture");
            Check(quickConnectApplied && ConnectionSettings.Read(uiSettingsPath).Kind == ConnectionKind.SynologyQuickConnect
                && address.Kind == ConnectionKind.SynologyQuickConnect && address.Input.ReadOnly && address.Input.Visible
                && history.Read().Any(item => item.Url == quickConnectUrl && item.Kind == ConnectionKind.SynologyQuickConnect),
                "A selected QuickConnect connection persists its explicit type in the read-only address bar");
            bool directApplied = false;
            core = await BrowserFixtureNavigation.RunAsync(window, fixture, fixtureHosts,
                delegate { directApplied = window.ApplyConnection(alternate); }, alternateUrl, "NAS Desk local fixture");
            Check(directApplied && ConnectionSettings.Read(uiSettingsPath).Kind == ConnectionKind.DirectHttps
                && address.Kind == ConnectionKind.DirectHttps && address.Input.ReadOnly && !address.TypePicker.Enabled
                && history.ReadEntries()[0].Name == "Selected fixture NAS",
                "Switching back to a generic HTTPS NAS preserves its name and never enables direct address editing");
        }
    }
}
