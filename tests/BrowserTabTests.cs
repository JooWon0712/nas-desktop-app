using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class BrowserTabTests
    {
        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string fixture, Action<bool, string> check)
        {
            HashSet<MainWindow> originalTabs = new HashSet<MainWindow>(window.BrowserTabs);
            int topLevelWindows = TopLevelWindowCount();
            string source = core.Source;
            string id = Guid.NewGuid().ToString("N").Substring(0, 8);
            string photosName = "fixture_photos_" + id;
            string driveName = "fixture_drive_" + id;
            string blankName = "fixture_staged_" + id;
            // Prepare synthetic routing before the new Core is connected to
            // window.open. Mapping after NewWindow assignment can be too late
            // for its first request even while an extra SDK deferral is held.
            Action<MainWindow> prepareTab = delegate(MainWindow tab)
            {
                tab.Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("nas-desk.test", fixture,
                    CoreWebView2HostResourceAccessKind.DenyCors);
            };
            window.BrowserTabPrepared += prepareTab;

            try
            {
                await core.ExecuteScriptAsync("window.tabFixtureMarker='primary-kept';window.tabFixturePhotos=window.open('/photos.html','" + photosName + "');void(0);");
                MainWindow photos = await NewTab(window, originalTabs, "Direct Photos-style tab");
                await WaitFor(() => photos.Browser.CoreWebView2.DocumentTitle == "Photos tab fixture", "Photos fixture document", () => Diagnostic(photos));
                check(!photos.TopLevel && window.Contains(photos) && TopLevelWindowCount() == topLevelWindows
                    && Object.ReferenceEquals(window.ActiveBrowserTab, photos) && core.Source == source
                    && await core.ExecuteScriptAsync("window.tabFixtureMarker") == "\"primary-kept\"",
                    "A Photos-style window.open creates an active in-app tab without another top-level window or replacing DSM");

                HashSet<MainWindow> beforeDrive = new HashSet<MainWindow>(window.BrowserTabs);
                await core.ExecuteScriptAsync("window.tabFixtureDrive=window.open('/drive.html','" + driveName + "');void(0);");
                MainWindow drive = await NewTab(window, beforeDrive, "Direct Drive-style tab");
                await WaitFor(() => drive.Browser.CoreWebView2.DocumentTitle == "Drive tab fixture", "Drive fixture document", () => Diagnostic(drive));
                check(!drive.TopLevel && window.Contains(drive) && TopLevelWindowCount() == topLevelWindows
                    && Object.ReferenceEquals(window.ActiveBrowserTab, drive) && core.Source == source,
                    "A Drive-style link opens beside Photos in the same application window");
                check(photos.Browser.CoreWebView2.Profile.ProfileName == core.Profile.ProfileName
                    && drive.Browser.CoreWebView2.Profile.ProfileName == core.Profile.ProfileName
                    && (await photos.Browser.CoreWebView2.ExecuteScriptAsync("document.cookie")).Contains("nas_desk_test=persisted")
                    && await drive.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')") == "\"persisted\"",
                    "NAS application tabs share their opener's existing login cookies and web storage");
                int blockedResources = photos.BlockedRequests;
                await photos.Browser.CoreWebView2.ExecuteScriptAsync("window.tabFixtureBlockedImage=new Image();window.tabFixtureBlockedImage.src='https://outside-tab-resource.invalid/pixel';");
                await WaitFor(() => photos.BlockedRequests > blockedResources, "Application tab blocks external subresources");
                check(photos.Browser.CoreWebView2.Source.EndsWith("photos.html", StringComparison.Ordinal) && photos.BlockedRequests > blockedResources,
                    "New NAS application tabs retain the default external-resource isolation policy");

                await photos.Browser.CoreWebView2.ExecuteScriptAsync("window.tabFixtureState='photos-kept';");
                await drive.Browser.CoreWebView2.ExecuteScriptAsync("window.tabFixtureState='drive-kept';");
                window.SelectBrowserTab(photos);
                window.SelectBrowserTab(drive);
                check(Object.ReferenceEquals(window.ActiveBrowserTab, drive)
                    && await photos.Browser.CoreWebView2.ExecuteScriptAsync("window.tabFixtureState") == "\"photos-kept\""
                    && await drive.Browser.CoreWebView2.ExecuteScriptAsync("window.tabFixtureState") == "\"drive-kept\""
                    && await core.ExecuteScriptAsync("window.tabFixtureMarker") == "\"primary-kept\"",
                    "Switching application tabs preserves each live document and the DSM document");

                int beforeReuse = window.BrowserTabs.Count;
                await core.ExecuteScriptAsync("window.tabFixturePhotosAgain=window.open('/photos.html?reused=1','" + photosName + "');void(0);");
                await WaitFor(() => photos.Browser.CoreWebView2.Source.EndsWith("photos.html?reused=1", StringComparison.Ordinal)
                    && photos.Browser.CoreWebView2.DocumentTitle == "Photos tab fixture", "Named Photos target reuse", () => Diagnostic(photos));
                check(window.BrowserTabs.Count == beforeReuse && TopLevelWindowCount() == topLevelWindows
                    && await core.ExecuteScriptAsync("window.tabFixturePhotosAgain===window.tabFixturePhotos") == "true",
                    "Opening the same named NAS application reuses its existing browser tab and WindowProxy");

                HashSet<MainWindow> beforeBlank = new HashSet<MainWindow>(window.BrowserTabs);
                await core.ExecuteScriptAsync("window.tabFixtureStaged=window.open('about:blank','" + blankName + "');void(0);");
                MainWindow staged = await NewTab(window, beforeBlank, "Blank application staging tab");
                bool opener = await core.ExecuteScriptAsync("window.tabFixtureStaged.document.title='Staged NAS app';window.tabFixtureStaged.opener===window;") == "true";
                await staged.Browser.CoreWebView2.ExecuteScriptAsync("window.opener.tabFixtureReply='opener-ok';");
                check(opener && await core.ExecuteScriptAsync("window.tabFixtureReply") == "\"opener-ok\""
                    && staged.Browser.CoreWebView2.DocumentTitle == "Staged NAS app" && core.Source == source,
                    "Blank tabs preserve the returned window reference, document access and window.opener communication");
                await core.ExecuteScriptAsync("window.tabFixtureStaged.location='/drive.html?staged=1';");
                await WaitFor(() => staged.Browser.CoreWebView2.DocumentTitle == "Drive tab fixture", "Deferred application tab navigation", () => Diagnostic(staged));
                check(staged.Browser.CoreWebView2.Source.EndsWith("drive.html?staged=1", StringComparison.Ordinal)
                    && await staged.Browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')") == "\"persisted\""
                    && core.Source == source,
                    "An about:blank application can navigate later without losing its session or replacing DSM");
                await staged.Browser.CoreWebView2.ExecuteScriptAsync("window.close();");
                await WaitFor(() => staged.IsDisposed && !window.BrowserTabs.Contains(staged), "Script closes its own application tab");
                check(!window.IsDisposed && window.Ready && photos.Ready && drive.Ready,
                    "window.close removes only its own tab while DSM and other NAS applications keep running");

                window.SelectBrowserTab(drive);
                window.CloseBrowserTab(drive);
                await WaitFor(() => drive.IsDisposed && !window.BrowserTabs.Contains(drive), "Selected tab close");
                check(!window.IsDisposed && photos.Ready && core.Source == source
                    && await core.ExecuteScriptAsync("window.tabFixtureDrive.closed") == "true",
                    "Closing the selected application tab disposes its WebView and updates the opener's closed state");
                int beforePrimaryClose = window.BrowserTabs.Count;
                window.CloseBrowserTab(window);
                check(!window.IsDisposed && window.BrowserTabs.Count == beforePrimaryClose && window.BrowserTabs.Contains(window),
                    "The primary DSM tab cannot accidentally close the whole application through a tab-close action");

                HashSet<MainWindow> beforeNested = new HashSet<MainWindow>(window.BrowserTabs);
                await photos.Browser.CoreWebView2.ExecuteScriptAsync("window.tabFixtureNested=window.open('/drive.html?nested=1','fixture_nested_" + id + "');void(0);");
                MainWindow nested = await NewTab(window, beforeNested, "Nested application tab");
                await WaitFor(() => nested.Browser.CoreWebView2.DocumentTitle == "Drive tab fixture", "Nested fixture document", () => Diagnostic(nested));
                check(!nested.TopLevel && window.Contains(nested) && TopLevelWindowCount() == topLevelWindows
                    && Object.ReferenceEquals(window.ActiveBrowserTab, nested),
                    "A NAS application opening another window adds its tab to the original application window");
                window.CloseBrowserTab(photos);
                await WaitFor(() => photos.IsDisposed && nested.IsDisposed && window.BrowserTabs.Count == originalTabs.Count,
                    "Closing an opener cleans up its nested application tab");
                check(!window.IsDisposed && window.Ready && core.Source == source,
                    "Closing an application and its nested tab leaves the primary DSM session running");

                int beforeBlocked = window.BrowserTabs.Count;
                await core.ExecuteScriptAsync("window.open('https://outside-popup.invalid/','fixture_external_" + id
                    + "');window.open('http://outside-popup.invalid/','fixture_insecure_" + id + "');void(0);");
                check(window.BrowserTabs.Count == beforeBlocked && TopLevelWindowCount() == topLevelWindows
                    && core.Source == source && await core.ExecuteScriptAsync("window.tabFixtureMarker") == "\"primary-kept\"",
                    "Automatic untrusted-origin and insecure-scheme popups create no application tab or extra window");
            }
            finally
            {
                window.BrowserTabPrepared -= prepareTab;
                foreach (MainWindow tab in window.BrowserTabs.Where(item => !originalTabs.Contains(item)).ToArray())
                    if (!tab.IsDisposed) window.CloseBrowserTab(tab);
                if (!window.IsDisposed) window.SelectBrowserTab(window);
            }
        }

        private static async Task<MainWindow> NewTab(MainWindow window, HashSet<MainWindow> before, string description)
        {
            MainWindow result = null;
            await WaitFor(() => {
                result = window.BrowserTabs.FirstOrDefault(item => !before.Contains(item) && !item.IsDisposed && item.Ready);
                return result != null;
            }, description, () => " [tabs=" + window.BrowserTabs.Count + "; primary=" + Diagnostic(window) + "]");
            return result;
        }

        private static int TopLevelWindowCount()
        {
            return Application.OpenForms.Cast<Form>().Count(form => form.TopLevel && !form.IsDisposed);
        }

        private static string Diagnostic(MainWindow tab)
        {
            try
            {
                CoreWebView2 core = tab.Browser.CoreWebView2;
                return " [ready=" + tab.Ready + "; disposed=" + tab.IsDisposed + "; source=" + (core == null ? "none" : core.Source)
                    + "; title=" + (core == null ? "none" : core.DocumentTitle) + "; failure=" + tab.IsFailureVisible
                    + "; detail=" + tab.FailureView.ErrorDetail + "]";
            }
            catch (Exception error) { return " [diagnostic unavailable: " + error.GetType().Name + "]"; }
        }

        private static async Task WaitFor(Func<bool> condition, string description, Func<string> diagnostic = null)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(description + (diagnostic == null ? String.Empty : diagnostic()));
                await Task.Delay(25);
            }
        }
    }
}
