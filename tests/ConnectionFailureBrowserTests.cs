using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class ConnectionFailureBrowserTests
    {
        internal static async Task RunAsync(string output, string fixture, Action<bool, string> check)
        {
            // Reserve and release a loopback-only port to reproduce a refused
            // connection without contacting a NAS or any external host.
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally { listener.Stop(); }
            string failedAddress = "https://127.0.0.1:" + port + "/";
            string directory = Path.Combine(Path.GetFullPath(output), "connection-failure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string settingsPath = Path.Combine(directory, "connection.json");
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate(failedAddress, ConnectionKind.DirectHttps, out settings))
                throw new Exception("Cannot construct the isolated connection failure fixture.");
            settings.Save(settingsPath);

            MainWindow window = null;
            try
            {
                window = new MainWindow(settings, Path.Combine(directory, "profile"), true, settingsPath);
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "Failure fixture WebView initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = window.Browser.CoreWebView2;
                var originalBrowser = window.Browser;
                int failures = 0;
                bool retryWasHidden = false;
                bool observeRetry = false;
                CoreWebView2WebErrorStatus lastError = CoreWebView2WebErrorStatus.Unknown;
                ulong loopbackNavigationId = 0;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (e.Uri == failedAddress) loopbackNavigationId = e.NavigationId;
                    if (observeRetry && e.Uri == failedAddress)
                        retryWasHidden = window.IsFailureVisible && !window.Browser.Visible
                            && !window.FailureView.RetryButton.Enabled;
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.NavigationId == loopbackNavigationId && !e.IsSuccess
                        && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    {
                        failures++;
                        lastError = e.WebErrorStatus;
                    }
                };
                core.Navigate(failedAddress);
                await WaitFor(() => window.IsFailureVisible && failures > 0, "Closed loopback port failure screen");
                bool nativeGuidance = window.FailureView.ErrorDetail == lastError.ToString()
                    && window.FailureView.ErrorDescription.Contains("포트");
                check(nativeGuidance,
                    "An unavailable loopback HTTPS destination displays app guidance for the actual native failure"
                        + " [status=" + lastError + "; detail=" + window.FailureView.ErrorDetail
                        + (nativeGuidance ? "]" : "; title=" + window.FailureView.ErrorTitle + "]"));
                check(window.Ready && !window.Browser.Visible && window.FailureView.Visible
                    && !core.Settings.IsBuiltInErrorPageEnabled
                    && Object.ReferenceEquals(originalBrowser, window.Browser),
                    "The custom failure surface hides the native browser error page without disposing its WebView");

                int beforeRetry = failures;
                observeRetry = true;
                window.FailureView.RetryButton.PerformClick();
                await WaitFor(() => failures > beforeRetry && window.IsFailureVisible
                    && window.FailureView.RetryButton.Enabled, "Retry returns to an actionable connection failure");
                observeRetry = false;
                check(retryWasHidden && !window.Browser.Visible && window.Ready
                    && window.FailureView.ErrorDetail == lastError.ToString(),
                    "Retry keeps the failed browser hidden while loading and restores the retry action after another failure");

                AddressModalBrowserTests.InModal(window, delegate { window.FailureView.EditAddressButton.PerformClick(); }, delegate(AddressHistoryDialog dialog)
                {
                    check(dialog.Modal && window.Address.Input.ReadOnly && window.IsFailureVisible && core.Source == failedAddress,
                        "The failure screen opens address history while preserving its read-only address and failed connection");
                });

                const string recovery = "https://failure-recovery.test/index.html";
                core.SetVirtualHostNameToFolderMapping("failure-recovery.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
                window.ApplyConnection(new ConnectionSettings { Url = recovery, Type = "https" });
                await WaitFor(() => core.Source == recovery && core.DocumentTitle == "NAS Desk local fixture"
                    && !window.IsFailureVisible, "Connection failure recovery through the address editor");
                check(window.Browser.Visible && !window.FailureView.Visible && window.Ready
                    && Object.ReferenceEquals(originalBrowser, window.Browser)
                    && Object.ReferenceEquals(core, window.Browser.CoreWebView2)
                    && window.Address.Input.ReadOnly && ConnectionSettings.Read(settingsPath).Url == recovery,
                    "A valid replacement address restores the NAS document and the same live WebView after failure");

                string recoveryHtml = File.ReadAllText(Path.Combine(fixture, "index.html"));
                EventHandler<CoreWebView2WebResourceRequestedEventArgs> respond = delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    Uri target;
                    if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out target)
                        || target.Host != "failure-recovery.test") return;
                    int code = 200;
                    string html = recoveryHtml;
                    if (target.AbsolutePath.StartsWith("/status-", StringComparison.Ordinal))
                    {
                        if (!Int32.TryParse(target.AbsolutePath.Substring(8), out code)) code = 400;
                        html = "<!doctype html><meta charset='utf-8'><title>NAS response fixture " + code
                            + "</title><p>Controlled response body. No device login.</p>";
                    }
                    // These synthetic responses deliberately contain no challenge
                    // header and never request or submit authentication credentials.
                    e.Response = core.Environment.CreateWebResourceResponse(
                        new MemoryStream(Encoding.UTF8.GetBytes(html)), code, "Fixture response", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                };
                core.AddWebResourceRequestedFilter("https://failure-recovery.test/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += respond;
                // Mapping applies per WebView. For these response-status checks,
                // supply every same-host response from the handler instead.
                core.ClearVirtualHostNameToFolderMapping("failure-recovery.test");
                try
                {
                    foreach (int code in new[] { 503, 404 })
                    {
                        string target = "https://failure-recovery.test/status-" + code;
                        core.Navigate(target);
                        await WaitFor(() => core.Source == target && window.IsFailureVisible
                            && window.FailureView.ErrorDetail == "HTTP " + code, "Custom HTTP failure " + code);
                        check(!window.Browser.Visible && window.FailureView.RetryButton.Enabled,
                            "A controlled HTTP " + code + " response displays the app failure screen with its exact status");
                        if (code == 503)
                        {
                            bool cancelled = false;
                            bool busyBeforeCancellation = false;
                            ulong retryId = 0;
                            EventHandler<CoreWebView2NavigationStartingEventArgs> trackRetry = delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                            {
                                if (e.Uri == recovery)
                                {
                                    retryId = e.NavigationId;
                                    busyBeforeCancellation = window.IsFailureVisible
                                        && !window.Browser.Visible && !window.FailureView.RetryButton.Enabled;
                                    e.Cancel = true;
                                }
                            };
                            EventHandler<CoreWebView2NavigationCompletedEventArgs> trackCancellation = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                            {
                                if (e.NavigationId == retryId && e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                                    cancelled = true;
                            };
                            core.NavigationStarting += trackRetry;
                            core.NavigationCompleted += trackCancellation;
                            try
                            {
                                window.FailureView.RetryButton.PerformClick();
                                await WaitFor(() => cancelled && window.FailureView.RetryButton.Enabled,
                                    "Canceled retry restores the retry action");
                                check(busyBeforeCancellation && window.IsFailureVisible && !window.Browser.Visible
                                    && window.FailureView.ErrorDetail == "HTTP 503",
                                    "Canceling an actual retry navigation preserves the failure screen and re-enables retry");
                            }
                            finally
                            {
                                core.NavigationStarting -= trackRetry;
                                core.NavigationCompleted -= trackCancellation;
                            }
                        }
                    }
                    foreach (int code in new[] { 401, 403 })
                    {
                        string target = "https://failure-recovery.test/status-" + code;
                        core.Navigate(target);
                        await WaitFor(() => core.Source == target && core.DocumentTitle == "NAS response fixture " + code
                            && !window.IsFailureVisible, "Preserved NAS authorization response " + code);
                        check(window.Browser.Visible && !window.FailureView.Visible,
                            "A controlled HTTP " + code + " NAS response remains visible instead of being replaced by connection guidance");
                    }
                    core.Navigate(recovery);
                    await WaitFor(() => core.Source == recovery && core.DocumentTitle == "NAS Desk local fixture"
                        && !window.IsFailureVisible, "Final successful response after connection and HTTP failures");
                    check(window.Browser.Visible && window.Ready && Object.ReferenceEquals(core, window.Browser.CoreWebView2),
                        "A successful response remains usable after connection, server and authorization responses");
                }
                finally { core.WebResourceRequested -= respond; }
            }
            finally
            {
                if (window != null) { window.RequestExit(); window.Dispose(); }
            }
        }

        private static async Task WaitFor(Func<bool> ready, string description)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!ready())
            {
                if (elapsed.ElapsedMilliseconds > 12000) throw new TimeoutException(description);
                await Task.Delay(25);
            }
        }
    }
}
