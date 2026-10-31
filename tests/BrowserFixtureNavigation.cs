using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NasDesk
{
    // Virtual-host mappings belong to one WebView. Install them before the
    // app navigates a replacement control after selecting another NAS profile.
    internal static class BrowserFixtureNavigation
    {
        internal static async Task<CoreWebView2> RunAsync(MainWindow window, string pages, IEnumerable<string> hosts,
            Action trigger, string target, string title)
        {
            Control viewport = window.Browser.Parent;
            WebView2 originalBrowser = window.Browser;
            bool replacementNavigated = false;
            List<WebView2> browsers = new List<WebView2>();
            List<CoreWebView2> cores = new List<CoreWebView2>();
            CoreWebView2 loadedCore = null;
            Action<WebView2> install = null;
            EventHandler<CoreWebView2NavigationCompletedEventArgs> completed = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                CoreWebView2 core = sender as CoreWebView2;
                if (core != null && e.IsSuccess && core.Source == target) loadedCore = core;
            };
            install = delegate(WebView2 browser)
            {
                CoreWebView2 core = browser.CoreWebView2;
                if (core == null || cores.Contains(core)) return;
                foreach (string host in hosts) core.SetVirtualHostNameToFolderMapping(host, pages, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationCompleted += completed;
                cores.Add(core);
            };
            EventHandler<CoreWebView2InitializationCompletedEventArgs> initialized = delegate(object sender, CoreWebView2InitializationCompletedEventArgs e)
            {
                if (e.IsSuccess) install((WebView2)sender);
            };
            Action<WebView2> attach = delegate(WebView2 browser)
            {
                if (browser == null || browsers.Contains(browser)) return;
                browsers.Add(browser);
                browser.CoreWebView2InitializationCompleted += initialized;
                install(browser);
            };
            ControlEventHandler added = delegate(object sender, ControlEventArgs e)
            {
                if (Object.ReferenceEquals(e.Control, window.Browser)) attach(window.Browser);
            };
            viewport.ControlAdded += added;
            attach(window.Browser);
            try
            {
                trigger();
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (true)
                {
                    if (window.InitializationError != null) throw window.InitializationError;
                    // testMode deliberately leaves a freshly initialized view
                    // on local HTML instead of contacting its saved NAS URL.
                    // Resume this mapped fixture only after the replacement is ready.
                    if (window.Ready && !replacementNavigated && !Object.ReferenceEquals(originalBrowser, window.Browser))
                    {
                        replacementNavigated = true;
                        window.Browser.CoreWebView2.Navigate(target);
                    }
                    if (window.Ready && window.Browser.CoreWebView2 != null
                        && Object.ReferenceEquals(loadedCore, window.Browser.CoreWebView2)
                        && loadedCore.Source == target && (title == null || loadedCore.DocumentTitle == title)) return loadedCore;
                    if (DateTime.UtcNow > deadline)
                    {
                        CoreWebView2 current = window.Browser.CoreWebView2;
                        throw new TimeoutException("Fixture connection did not load: " + target
                            + " [ready=" + window.Ready + "; initialized=" + (current != null)
                            + "; source=" + (current == null ? "" : current.Source)
                            + "; title=" + (current == null ? "" : current.DocumentTitle)
                            + "; installed=" + cores.Count + "; completedCurrent=" + Object.ReferenceEquals(loadedCore, current)
                            + "; failure=" + window.IsFailureVisible + "; detail=" + window.FailureView.ErrorDetail + "]");
                    }
                    await Task.Delay(40);
                }
            }
            finally
            {
                viewport.ControlAdded -= added;
                foreach (WebView2 browser in browsers)
                    if (!browser.IsDisposed) browser.CoreWebView2InitializationCompleted -= initialized;
                foreach (CoreWebView2 core in cores)
                    try { core.NavigationCompleted -= completed; }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                    catch (System.Runtime.InteropServices.COMException) { }
            }
        }
    }
}
