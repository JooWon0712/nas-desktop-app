using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class BrowserTabSecurityTests
    {
        internal static async Task RunAsync(string output, string fixture, Action<bool, string> check)
        {
            const string endpoint = "https://tab-policy-nas.test:5001/index.html";
            const string delayedTarget = "https://tab-policy-delayed.test:5001/index.html";
            string directory = Path.Combine(Path.GetFullPath(output), "ts-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            string settingsPath = Path.Combine(directory, "connection.json");
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate("tab-policy-fixture", ConnectionKind.SynologyQuickConnect, out settings))
                throw new Exception("Invalid tab security fixture.");
            settings.Save(settingsPath);
            TaskCompletionSource<bool> delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool verifierStarted = false;
            ConnectionSession session = new ConnectionSession(settings.Url, settings.Kind,
                delegate(string target, CancellationToken cancellation)
                {
                    if (target == delayedTarget) { verifierStarted = true; return delayed.Task; }
                    return Task.FromResult(target == endpoint);
                });
            MainWindow window = null;
            Task<string> pendingScript = null;
            Action<MainWindow> prepareTab = delegate(MainWindow tab)
            {
                // Map even the canceled destination before NewWindow assignment:
                // a regression must still stay inside local synthetic documents.
                foreach (string host in new[] { "tab-policy-nas.test", "tab-policy-delayed.test" })
                    tab.Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(host, fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            };
            try
            {
                if (!await session.VerifyAndAllowAsync(endpoint)) throw new Exception("Fixture NAS endpoint was not verified.");
                window = new MainWindow(settings, Path.Combine(directory, "profile"), true, settingsPath, session);
                window.BrowserTabPrepared += prepareTab;
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "Tab policy fixture initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = window.Browser.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("tab-policy-nas.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
                core.Navigate(endpoint);
                await WaitFor(() => core.Source == endpoint && core.DocumentTitle == "NAS Desk local fixture", "Verified local NAS document");
                await core.ExecuteScriptAsync("window.tabPolicyMarker='primary-kept';");
                int topLevelWindows = TopLevelWindowCount();
                window.SetExternalContentAllowed(true);

                // window.open can keep ExecuteScriptAsync pending until its
                // NewWindowRequested deferral completes; do not await it yet.
                pendingScript = core.ExecuteScriptAsync("window.tabPolicyPending=window.open('" + delayedTarget
                    + "','tab_policy_pending');void(0);");
                await WaitFor(() => verifierStarted, "Pending application-tab endpoint verification");
                window.SetExternalContentAllowed(false);
                delayed.SetResult(true);
                await CompleteScript(pendingScript, "Canceled application-tab request completion");
                check(window.BrowserTabs.Count == 1 && Object.ReferenceEquals(window.ActiveBrowserTab, window)
                    && TopLevelWindowCount() == topLevelWindows && window.Ready && core.Source == endpoint
                    && core.DocumentTitle == "NAS Desk local fixture"
                    && await core.ExecuteScriptAsync("window.tabPolicyMarker") == "\"primary-kept\"",
                    "A late QuickConnect popup approval cannot append a tab after the NAS security policy changes");

                foreach (bool allowed in new[] { true, false })
                {
                    MainWindow tab = null;
                    await core.ExecuteScriptAsync("window.tabPolicyLive=window.open('about:blank','tab_policy_live_"
                        + allowed.ToString().ToLowerInvariant() + "');void(0);");
                    await WaitFor(() => {
                        tab = window.BrowserTabs.FirstOrDefault(item => !Object.ReferenceEquals(item, window));
                        return tab != null && tab.Ready && tab.Parent != null;
                    }, "Live application tab before security policy change");
                    window.SetExternalContentAllowed(allowed);
                    await WaitFor(() => tab.IsDisposed && window.BrowserTabs.Count == 1, "Security policy closes existing application tab");
                    check(window.Ready && !window.IsDisposed && Object.ReferenceEquals(window.ActiveBrowserTab, window)
                        && TopLevelWindowCount() == topLevelWindows && core.Source == endpoint
                        && await core.ExecuteScriptAsync("window.tabPolicyMarker") == "\"primary-kept\""
                        && await core.ExecuteScriptAsync("window.tabPolicyLive.closed") == "true",
                        "Changing external content to " + allowed + " closes existing application tabs while retaining the primary NAS document");
                }
            }
            finally
            {
                delayed.TrySetCanceled();
                if (pendingScript != null) ObserveFailure(pendingScript);
                if (window != null)
                {
                    window.BrowserTabPrepared -= prepareTab;
                    window.RequestExit();
                    window.Dispose();
                }
                session.Dispose();
            }
        }

        private static async Task CompleteScript(Task<string> task, string description)
        {
            if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new TimeoutException(description);
            await task;
        }

        private static void ObserveFailure(Task task)
        {
            task.ContinueWith(delegate(Task failed)
            {
                if (failed.Exception != null) failed.Exception.Handle(delegate { return true; });
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static int TopLevelWindowCount()
        {
            return Application.OpenForms.Cast<Form>().Count(form => form.TopLevel && !form.IsDisposed);
        }

        private static async Task WaitFor(Func<bool> condition, string description)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(description);
                await Task.Delay(25);
            }
        }
    }
}
