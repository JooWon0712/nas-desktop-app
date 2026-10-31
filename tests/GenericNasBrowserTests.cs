using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class GenericNasBrowserTests
    {
        internal static async Task RunAsync(string output, string fixture, Action<bool, string> check)
        {
            // Synthetic routing examples only. These do not verify any vendor's
            // real login implementation, cloud service, or default port settings.
            string[] inputs = new[] {
                "https://generic-qnap.test:8443/cgi-bin/fixture.html",
                "https://generic-adm.test:8001/portal/fixture.html",
                "https://generic-truenas.test:443/ui/fixture.html"
            };
            string directory = Path.Combine(Path.GetFullPath(output), "generic-browser-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            for (int index = 0; index < inputs.Length; index++)
                await RunRouteAsync(directory, inputs[index], index + 1, check);
        }

        private static async Task RunRouteAsync(string directory, string input, int index, Action<bool, string> check)
        {
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate(input, ConnectionKind.DirectHttps, out settings))
                throw new Exception("Cannot construct direct HTTPS fixture settings.");

            string name = "route-" + index;
            string routeDirectory = Path.Combine(directory, name);
            string pages = Path.Combine(routeDirectory, "pages");
            Uri home = new Uri(settings.Url);
            string pageDirectory = Path.Combine(pages, home.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            pageDirectory = Path.GetDirectoryName(pageDirectory);
            Directory.CreateDirectory(pageDirectory);
            string title = "Generic HTTPS fixture " + index;
            string page = "<!doctype html><meta charset='utf-8'><title>" + title + "</title>" +
                "<script>document.cookie='generic_nas_fixture=" + name + "; max-age=600; path=/; secure; samesite=lax';" +
                "localStorage.setItem('generic_nas_fixture','" + name + "');</script>" +
                "<p>Synthetic direct HTTPS NAS interface. No device login.</p>";
            File.WriteAllText(Path.Combine(pageDirectory, "fixture.html"), page);
            File.WriteAllText(Path.Combine(pageDirectory, "popup.html"),
                "<!doctype html><title>Generic HTTPS popup fixture</title><p>Same-origin synthetic popup.</p>");
            string settingsPath = Path.Combine(routeDirectory, "connection.json");
            settings.Save(settingsPath);
            MainWindow window = null;
            MainWindow popup = null;
            try
            {
                window = new MainWindow(settings, Path.Combine(routeDirectory, "profile"), true, settingsPath);
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "Direct HTTPS fixture initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = window.Browser.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping(home.DnsSafeHost, pages, CoreWebView2HostResourceAccessKind.DenyCors);
                core.SetVirtualHostNameToFolderMapping("generic-unrelated.test", pages, CoreWebView2HostResourceAccessKind.DenyCors);
                core.Navigate(settings.Url);
                await WaitFor(() => core.Source == settings.Url && core.DocumentTitle == title,
                    "Direct HTTPS fixture port and nested path load");
                check(settings.Type == "https" && window.Address.Kind == ConnectionKind.DirectHttps
                    && window.Text == "NAS Desktop" && core.Source == settings.Url
                    && !core.Settings.AreHostObjectsAllowed && !core.Settings.IsWebMessageEnabled,
                    "Synthetic direct HTTPS " + name + " loads its configured host, port and path in the generic app");
                check((await core.ExecuteScriptAsync("document.cookie")).Contains("generic_nas_fixture=" + name)
                    && await core.ExecuteScriptAsync("localStorage.getItem('generic_nas_fixture')") == "\"" + name + "\"",
                    "Synthetic direct HTTPS " + name + " retains cookies and web storage");

                HashSet<Form> existing = new HashSet<Form>(Application.OpenForms.Cast<Form>());
                string popupUrl = new Uri(home, "popup.html").AbsoluteUri;
                CoreWebView2Deferral popupDeferral = null;
                EventHandler<CoreWebView2NewWindowRequestedEventArgs> deferPopup = delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    popupDeferral = e.GetDeferral();
                };
                core.NewWindowRequested += deferPopup;
                try
                {
                    Task opening = core.ExecuteScriptAsync("window.open('" + popupUrl + "','generic_popup_" + index + "');");
                    await WaitFor(() => {
                        popup = Application.OpenForms.Cast<Form>().OfType<MainWindow>().FirstOrDefault(form => !existing.Contains(form));
                        return popupDeferral != null && popup != null && popup.Ready;
                    }, "Same-origin generic HTTPS popup creation");
                    // Virtual-host mappings belong to each test WebView, not its
                    // shared profile. Supply the child's fixture before navigation.
                    popup.Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(home.DnsSafeHost, pages, CoreWebView2HostResourceAccessKind.DenyCors);
                    popupDeferral.Complete();
                    popupDeferral = null;
                    await opening;
                    await WaitFor(() => popup.Browser.CoreWebView2.DocumentTitle == "Generic HTTPS popup fixture", "Same-origin generic HTTPS popup page");
                }
                finally
                {
                    core.NewWindowRequested -= deferPopup;
                    if (popupDeferral != null) popupDeferral.Complete();
                }
                check((await popup.Browser.CoreWebView2.ExecuteScriptAsync("document.cookie")).Contains("generic_nas_fixture=" + name)
                    && await popup.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('generic_nas_fixture')") == "\"" + name + "\"",
                    "Synthetic direct HTTPS " + name + " popup shares its configured NAS cookies and storage");
                popup.RequestExit();
                popup.Dispose();
                popup = null;

                UriBuilder otherPort = new UriBuilder(home) { Port = home.Port + 1 };
                UriBuilder otherHost = new UriBuilder(home) { Host = "generic-unrelated.test" };
                foreach (string target in new[] { otherPort.Uri.AbsoluteUri, otherHost.Uri.AbsoluteUri })
                {
                    bool cancelled = false;
                    EventHandler<CoreWebView2NavigationStartingEventArgs> observe = delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                    {
                        if (e.Uri == target) cancelled = e.Cancel;
                    };
                    core.NavigationStarting += observe;
                    try
                    {
                        await core.ExecuteScriptAsync("location.href='" + target + "';");
                        await WaitFor(() => cancelled, "Unconfigured direct HTTPS origin cancelled");
                        check(core.Source == settings.Url && core.DocumentTitle == title,
                            "Synthetic direct HTTPS " + name + " blocks a different " +
                            (target == otherPort.Uri.AbsoluteUri ? "port" : "host") + " before replacing the NAS document");
                    }
                    finally { core.NavigationStarting -= observe; }
                }
            }
            finally
            {
                if (popup != null && !popup.IsDisposed) { popup.RequestExit(); popup.Dispose(); }
                if (window != null) { window.RequestExit(); window.Dispose(); }
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
