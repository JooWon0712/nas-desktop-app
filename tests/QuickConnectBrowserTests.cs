using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class QuickConnectBrowserTests
    {
        internal static async Task RunAsync(string output, string fixture, Action<bool, string> check)
        {
            const string home = "https://demo-nas.quickconnect.to/";
            const string bootstrap = home + "bootstrap.html";
            const string endpoint = "https://nas-endpoint.test:5001/dsm.html";
            const string unrelated = "https://another-nas.quickconnect.to/other.html";
            const string supersededEndpoint = "https://superseded-route.test:5001/ignored.html";
            const string keptPage = "https://nas-endpoint.test:5001/kept.html";
            const string delayedEndpoint = "https://late-endpoint.test:5001/old.html";
            const string replacement = "https://replacement-nas.test:5001/new.html";
            string testDirectory = Path.Combine(Path.GetFullPath(output), "quickconnect-browser-" + Guid.NewGuid().ToString("N"));
            string pages = Path.Combine(testDirectory, "pages");
            Directory.CreateDirectory(pages);
            File.WriteAllText(Path.Combine(pages, "bootstrap.html"), "<!doctype html><title>QC bootstrap fixture</title><script>location.replace('" + endpoint + "');</script>");
            File.WriteAllText(Path.Combine(pages, "dsm.html"), "<!doctype html><title>QC NAS fixture</title><p>Verified local NAS fixture.</p>");
            File.WriteAllText(Path.Combine(pages, "other.html"), "<!doctype html><title>Unrelated NAS fixture</title><p>This page must not load.</p>");
            File.WriteAllText(Path.Combine(pages, "ignored.html"), "<!doctype html><title>Superseded route fixture</title><p>This page must not replace a newer navigation.</p>");
            File.WriteAllText(Path.Combine(pages, "kept.html"), "<!doctype html><title>Kept NAS page fixture</title><p>A newer allowed navigation.</p>");
            File.WriteAllText(Path.Combine(pages, "old.html"), "<!doctype html><title>Stale NAS fixture</title><p>This page must not load after replacement.</p>");
            File.WriteAllText(Path.Combine(pages, "new.html"), "<!doctype html><title>Replacement NAS fixture</title><p>New configured connection.</p>");

            int verifiedRequests = 0;
            int rejectedRequests = 0;
            int endpointNavigations = 0;
            bool delayedStarted = false;
            bool supersededStarted = false;
            TaskCompletionSource<bool> superseded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConnectionSession session = new ConnectionSession(home, async delegate(string target, CancellationToken cancellation)
            {
                // Real verification yields to HTTPS I/O; preserve that event ordering without using a network.
                await Task.Yield();
                if (target == endpoint) { verifiedRequests++; return true; }
                if (target == supersededEndpoint) { supersededStarted = true; return await superseded.Task; }
                if (target == delayedEndpoint) { delayedStarted = true; return await delayed.Task; }
                rejectedRequests++;
                return false;
            });
            MainWindow window = null;
            try
            {
                string settingsPath = Path.Combine(testDirectory, "connection.json");
                ConnectionSettings settings = new ConnectionSettings { Url = home };
                settings.Save(settingsPath);
                window = new MainWindow(settings, Path.Combine(testDirectory, "profile"), true, settingsPath, session);
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "QuickConnect test window initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = window.Browser.CoreWebView2;
                foreach (string host in new[] { "demo-nas.quickconnect.to", "nas-endpoint.test", "another-nas.quickconnect.to", "superseded-route.test", "late-endpoint.test", "replacement-nas.test" })
                    core.SetVirtualHostNameToFolderMapping(host, pages, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (e.Uri == endpoint) endpointNavigations++;
                };

                core.Navigate(bootstrap);
                await WaitFor(() => core.Source == endpoint && core.DocumentTitle == "QC NAS fixture", "Verified QuickConnect cross-origin redirect");
                check(verifiedRequests == 1 && endpointNavigations >= 2,
                    "Browser cancels, verifies and resumes a QuickConnect redirect before loading the NAS page");
                check(window.Address.AddressText == home && settings.Url == home,
                    "Resolved NAS navigation preserves the user's configured QuickConnect address");

                await core.ExecuteScriptAsync("location.href='" + unrelated + "';");
                await WaitFor(() => rejectedRequests != 0, "Unrelated QuickConnect ID rejection");
                check(core.Source == endpoint && core.DocumentTitle == "QC NAS fixture",
                    "Failed identity verification leaves the existing NAS document unchanged");

                await core.ExecuteScriptAsync("location.href='" + supersededEndpoint + "';");
                await WaitFor(() => supersededStarted, "Endpoint verification pending before a newer allowed navigation");
                core.Navigate(keptPage);
                await WaitFor(() => core.Source == keptPage && core.DocumentTitle == "Kept NAS page fixture", "Newer allowed navigation during endpoint verification");
                check(!superseded.Task.IsCompleted,
                    "A newer allowed NAS page can load while endpoint verification is still pending");
                int supersededAttempts = 0;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (e.Uri == supersededEndpoint) supersededAttempts++;
                };
                superseded.SetResult(true);
                await Task.Yield();
                await Task.Delay(150);
                check(supersededAttempts == 0 && core.Source == keptPage && core.DocumentTitle == "Kept NAS page fixture",
                    "Late endpoint approval cannot overwrite a newer allowed page navigation");
                core.Navigate(endpoint);
                await WaitFor(() => core.Source == endpoint && core.DocumentTitle == "QC NAS fixture", "Original NAS page restored for connection-replacement test");

                await core.ExecuteScriptAsync("location.href='" + delayedEndpoint + "';");
                await WaitFor(() => delayedStarted, "Delayed endpoint verification started");
                check(core.Source == endpoint, "Pending endpoint verification cannot navigate before approval");

                window.Address.IsEditing = true;
                window.Address.Kind = ConnectionKind.DirectHttps;
                window.Address.AddressText = replacement;
                window.Address.ConnectButton.PerformClick();
                await WaitFor(() => core.Source == replacement && core.DocumentTitle == "Replacement NAS fixture", "Address editor replaces the connection during verification");
                check(ConnectionSettings.Read(settingsPath).Url == replacement && !window.Address.IsEditing,
                    "Address editor saves and loads a replacement NAS while an old verification is pending");

                int staleAttempts = 0;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (e.Uri == delayedEndpoint) staleAttempts++;
                };
                delayed.SetResult(true);
                await Task.Yield();
                await Task.Delay(150);
                check(staleAttempts == 0 && core.Source == replacement
                    && core.DocumentTitle == "Replacement NAS fixture" && window.Address.AddressText == replacement,
                    "Late verification completion never resumes the superseded connection or overwrites the new page"
                    + " [staleAttempts=" + staleAttempts + "; source=" + core.Source
                    + "; title=" + core.DocumentTitle + "; address=" + window.Address.AddressText
                    + "; failure=" + window.IsFailureVisible + "; detail=" + window.FailureView.ErrorDetail + "]");
            }
            finally
            {
                superseded.TrySetCanceled();
                delayed.TrySetCanceled();
                if (window != null) { window.RequestExit(); window.Dispose(); }
                session.Dispose();
            }
        }

        private static async Task WaitFor(Func<bool> ready, string description)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!ready())
            {
                if (elapsed.ElapsedMilliseconds > 10000) throw new TimeoutException(description);
                await Task.Delay(50);
            }
        }
    }
}
