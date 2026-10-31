using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class BrowserProfileIsolationTests
    {
        internal static async Task RunAsync(string output, Action<bool, string> check)
        {
            const string host = "isolated-nas.test";
            string folder;
            do { folder = Path.Combine(Path.GetFullPath(output), "pi-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }
            while (Directory.Exists(folder) || File.Exists(folder));
            string pages = Path.Combine(folder, "pages");
            string userData = Path.Combine(folder, "profile");
            string settingsPath = Path.Combine(folder, "connection.json");
            Directory.CreateDirectory(pages);
            File.WriteAllText(Path.Combine(pages, "index.html"),
                "<!doctype html><meta charset='utf-8'><title>Profile isolation fixture</title><p>Local profile fixture. No automatic storage writes.</p>");
            ConnectionSettings first, second;
            ConnectionSettings.TryCreate("https://" + host + ":8443/index.html", ConnectionKind.DirectHttps, out first);
            ConnectionSettings.TryCreate("https://" + host + ":9443/index.html", ConnectionKind.DirectHttps, out second);
            first.Save(settingsPath);
            MainWindow window = null;
            try
            {
                window = new MainWindow(new ConnectionSettings { Url = first.Url, Type = first.Type }, userData, true, settingsPath);
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "Profile fixture initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = await BrowserFixtureNavigation.RunAsync(window, pages, new[] { host },
                    delegate { window.Browser.CoreWebView2.Navigate(first.Url); }, first.Url, "Profile isolation fixture");
                string firstName = window.BrowserProfileName;
                string firstPath = core.Profile.ProfilePath;
                var firstBrowser = window.Browser;
                check(core.Profile.ProfileName == firstName && !String.Equals(firstName, "Default", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFullPath(core.Environment.UserDataFolder).TrimEnd(Path.DirectorySeparatorChar)
                        == Path.GetFullPath(userData).TrimEnd(Path.DirectorySeparatorChar)
                    && !window.TrayVisible && window.TrayAvailable,
                    "A test NAS uses its own named profile inside the isolated user-data folder without registering an OS tray icon");
                check(!core.Settings.IsPasswordAutosaveEnabled && !core.Settings.IsGeneralAutofillEnabled
                    && !core.Settings.AreHostObjectsAllowed && !core.Settings.IsWebMessageEnabled && !window.AllowExternalContent,
                    "New NAS profiles disable password and general autofill, native bridges and external content by default");
                await WriteState(core, host, "A");
                await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Geolocation,
                    new Uri(first.Url).GetLeftPart(UriPartial.Authority), CoreWebView2PermissionState.Allow);
                check(await Cookie(core, first.Url) == "A" && await State(core) == "\"A\""
                    && (await core.Profile.GetNonDefaultPermissionSettingsAsync()).Any(item => item.PermissionKind == CoreWebView2PermissionKind.Geolocation
                        && item.PermissionState == CoreWebView2PermissionState.Allow),
                    "The first local NAS fixture stores its own cookie, web storage and explicit permission state");

                HashSet<Form> before = new HashSet<Form>(Application.OpenForms.Cast<Form>());
                MainWindow popup = null;
                try
                {
                    await core.ExecuteScriptAsync("window.open('about:blank','isolated_profile_popup');");
                    await WaitFor(() => {
                        popup = Application.OpenForms.Cast<Form>().OfType<MainWindow>().FirstOrDefault(form => !before.Contains(form));
                        return popup != null && popup.Ready;
                    }, "Same-NAS profile popup");
                    check(popup.Browser.CoreWebView2.Profile.ProfileName == firstName
                        && popup.BrowserProfileName == firstName && await Cookie(popup.Browser.CoreWebView2, first.Url) == "A"
                        && await State(popup.Browser.CoreWebView2) == "\"A\"" && !popup.TrayVisible && !popup.TrayAvailable,
                        "A same-NAS popup shares only its opener's named profile, cookies and web storage without creating another tray icon");
                }
                finally
                {
                    if (popup != null && !popup.IsDisposed) { popup.RequestExit(); popup.Dispose(); }
                }

                core = await Select(window, pages, host, second);
                string secondName = window.BrowserProfileName;
                check(firstBrowser.IsDisposed && !Object.ReferenceEquals(firstBrowser, window.Browser)
                    && secondName != firstName && core.Profile.ProfileName == secondName && core.Profile.ProfilePath != firstPath
                    && Path.GetFullPath(core.Environment.UserDataFolder).TrimEnd(Path.DirectorySeparatorChar)
                        == Path.GetFullPath(userData).TrimEnd(Path.DirectorySeparatorChar),
                    "Selecting another port on the same host creates a different real profile while keeping the shared user-data folder");
                check(await Cookie(core, second.Url) == null && await State(core) == "null"
                    && !(await core.Profile.GetNonDefaultPermissionSettingsAsync()).Any(item => item.PermissionKind == CoreWebView2PermissionKind.Geolocation
                        && item.PermissionState == CoreWebView2PermissionState.Allow),
                    "A different NAS port cannot see the first profile's cookie, local storage or saved permission");
                await WriteState(core, host, "B");
                window.SetExternalContentAllowed(true);
                check(await Cookie(core, second.Url) == "B" && await State(core) == "\"B\"" && window.AllowExternalContent,
                    "The second NAS can keep independent cookie, storage and external-content preferences");

                core = await Select(window, pages, host, first);
                check(window.BrowserProfileName == firstName && core.Profile.ProfilePath == firstPath
                    && await Cookie(core, first.Url) == "A" && await State(core) == "\"A\"" && !window.AllowExternalContent,
                    "Returning to the first NAS restores its own saved session and strict external-content preference");
                var sameBrowser = window.Browser;
                core = await Select(window, pages, host, first);
                check(Object.ReferenceEquals(sameBrowser, window.Browser) && await Cookie(core, first.Url) == "A"
                    && await State(core) == "\"A\"",
                    "Reconnecting the same NAS identity retains its browser control and saved session");

                string saved = File.ReadAllText(settingsPath);
                await window.ClearCurrentLoginDataAsync();
                check(core.Source == "about:blank" && window.BrowserProfileName == firstName && window.Address.HistoryEnabled
                    && File.ReadAllText(settingsPath) == saved && window.Address.AddressText == first.Url
                    && await Cookie(core, first.Url) == null,
                    "Clearing the current NAS removes its cookies and leaves the page blank without changing the saved connection");
                core = await BrowserFixtureNavigation.RunAsync(window, pages, new[] { host },
                    delegate { window.Browser.CoreWebView2.Navigate(first.Url); }, first.Url, "Profile isolation fixture");
                string clearedCookie = await Cookie(core, first.Url);
                string clearedStorage = await State(core);
                var clearedPermissions = await core.Profile.GetNonDefaultPermissionSettingsAsync();
                check(clearedCookie == null && clearedStorage == "null"
                    && !clearedPermissions.Any(item => item.PermissionKind == CoreWebView2PermissionKind.Geolocation
                        && item.PermissionState == CoreWebView2PermissionState.Allow),
                    "An explicit reload after clearing cannot recover the deleted NAS cookies, local storage or saved permission"
                    + " [cookie=" + (clearedCookie ?? "null") + "; storage=" + clearedStorage
                    + "; permissions=" + String.Join(",", clearedPermissions.Select(item => item.PermissionKind + ":" + item.PermissionState)) + "]");

                core = await Select(window, pages, host, second);
                check(window.BrowserProfileName == secondName && await Cookie(core, second.Url) == "B"
                    && await State(core) == "\"B\"" && window.AllowExternalContent,
                    "Clearing one NAS profile preserves the other NAS session and its independent security preference");
            }
            finally
            {
                if (window != null) { window.RequestExit(); window.Dispose(); }
            }
        }

        private static async Task<CoreWebView2> Select(MainWindow window, string pages, string host, ConnectionSettings settings)
        {
            return await BrowserFixtureNavigation.RunAsync(window, pages, new[] { host }, delegate {
                if (!window.ApplyConnection(settings)) throw new Exception("Could not select the local profile fixture.");
            }, settings.Url, "Profile isolation fixture");
        }

        private static async Task WriteState(CoreWebView2 core, string host, string value)
        {
            CoreWebView2Cookie cookie = core.CookieManager.CreateCookie("isolated_session", value, host, "/");
            cookie.IsSecure = true;
            cookie.IsHttpOnly = true;
            cookie.Expires = DateTime.Now.AddDays(1);
            core.CookieManager.AddOrUpdateCookie(cookie);
            await core.ExecuteScriptAsync("localStorage.setItem('isolated_session','" + value + "');");
        }

        private static async Task<string> Cookie(CoreWebView2 core, string url)
        {
            CoreWebView2Cookie cookie = (await core.CookieManager.GetCookiesAsync(url)).FirstOrDefault(item => item.Name == "isolated_session");
            return cookie == null ? null : cookie.Value;
        }

        private static Task<string> State(CoreWebView2 core) { return core.ExecuteScriptAsync("localStorage.getItem('isolated_session')"); }

        private static async Task WaitFor(Func<bool> ready, string description)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(description);
                await Task.Delay(40);
            }
        }
    }
}
