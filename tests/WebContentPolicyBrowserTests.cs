using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class WebContentPolicyBrowserTests
    {
        internal static async Task RunAsync(string output, Action<bool, string> check)
        {
            string directory;
            do { directory = Path.Combine(Path.GetFullPath(output), "wc-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }
            while (Directory.Exists(directory) || File.Exists(directory));
            Directory.CreateDirectory(directory);
            string settingsPath = Path.Combine(directory, "connection.json");
            const string home = "https://nas-content-fixture.test/";
            const string external = "https://external-content-fixture.test/";
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate(home, ConnectionKind.DirectHttps, out settings)) throw new Exception("Invalid content fixture.");
            settings.Save(settingsPath);
            MainWindow window = null;
            try
            {
                window = new MainWindow(settings, Path.Combine(directory, "profile"), true, settingsPath);
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "Content policy WebView initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = window.Browser.CoreWebView2;
                Dictionary<string, int> responses = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, bool> frames = new Dictionary<string, bool>(StringComparer.Ordinal);
                int unexpectedRequests = 0;
                string workerTarget = external + "worker-fetch?case=" + Guid.NewGuid().ToString("N");
                ulong homeNavigationId = 0;
                bool homeCompleted = false, homeSucceeded = false, homeCanceledAtStart = false;
                int homeHttpStatus = 0;
                CoreWebView2WebErrorStatus homeWebError = CoreWebView2WebErrorStatus.Unknown;
                Func<string> homeDiagnostic = delegate
                {
                    int mockStatus;
                    responses.TryGetValue(home, out mockStatus);
                    return " [navigation=" + homeNavigationId + "; completed=" + homeCompleted + "; success=" + homeSucceeded
                        + "; http=" + homeHttpStatus + "; webError=" + homeWebError + "; canceledAtStart=" + homeCanceledAtStart
                        + "; ready=" + window.Ready + "; failureVisible=" + window.IsFailureVisible + "; mockedHttp=" + mockStatus
                        + "; sourceMatches=" + (core.Source == home) + "; titleMatches=" + (core.DocumentTitle == "NAS content policy fixture") + "]";
                };
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (e.Uri != home) return;
                    homeNavigationId = e.NavigationId;
                    homeCanceledAtStart = e.Cancel;
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (homeNavigationId == 0 || e.NavigationId != homeNavigationId) return;
                    homeSucceeded = e.IsSuccess;
                    homeHttpStatus = e.HttpStatusCode;
                    homeWebError = e.WebErrorStatus;
                    homeCompleted = true;
                };

                // Production registered its policy first. Observe its response
                // without replacing a block. Every unblocked fixture request is
                // also answered locally, including requests a broken policy
                // would have allowed, so this test never resolves these names.
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                core.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    string value = e.Request.Uri;
                    if (e.Response != null)
                    {
                        responses[value] = e.Response.StatusCode;
                        return;
                    }
                    Uri uri;
                    bool fixture = Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https"
                        && (uri.Host == "nas-content-fixture.test" || uri.Host == "external-content-fixture.test");
                    if (!fixture)
                    {
                        unexpectedRequests++;
                        e.Response = Response(core, "Unexpected fixture request blocked", 403, "text/plain");
                        responses[value] = 403;
                        return;
                    }
                    string body, contentType;
                    if (uri.Host == "external-content-fixture.test") { body = "EXTERNAL_OK"; contentType = "text/plain"; }
                    else if (uri.AbsolutePath == "/worker.js")
                    {
                        body = "fetch(" + Json(workerTarget) + ", {cache:'no-store'}).then(function(r){return r.text().then(function(t){postMessage(r.status+':'+t);});}).catch(function(){postMessage('fetch-error');});";
                        contentType = "application/javascript";
                    }
                    else if (uri.AbsolutePath == "/")
                    {
                        body = "<!doctype html><meta charset='utf-8'><title>NAS content policy fixture</title><p>Only synthetic local responses.</p>";
                        contentType = "text/html";
                    }
                    else { body = "NAS_OK"; contentType = "text/plain"; }
                    e.Response = Response(core, body, 200, contentType);
                    responses[value] = 200;
                };
                core.FrameNavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { frames[e.Uri] = e.Cancel; };
                core.Navigate(home);
                // Source and title can change before production receives the
                // navigation completion that clears its previous failure UI.
                // Await this exact document's real completion, never the initial
                // test NavigateToString completion or a canceled predecessor.
                await WaitFor(() => homeCompleted, "Mocked NAS document completion", homeDiagnostic);
                check(homeSucceeded && homeHttpStatus == 200 && !homeCanceledAtStart
                    && core.Source == home && core.DocumentTitle == "NAS content policy fixture"
                    && window.Ready && !window.IsFailureVisible && responses.ContainsKey(home) && responses[home] == 200
                    && !core.Settings.IsPasswordAutosaveEnabled && !core.Settings.IsGeneralAutofillEnabled,
                    "A mocked NAS loads under the production content filter with password saving and general autofill disabled" + homeDiagnostic());

                string sameNas = home + "api/status?case=" + Guid.NewGuid().ToString("N");
                string sameResult = await Fetch(core, sameNas, "sameNasResult");
                check(sameResult == "\"200:NAS_OK\"" && responses.ContainsKey(sameNas) && responses[sameNas] == 200,
                    "The strict browser policy permits a real same-NAS fetch through a locally mocked response");

                string strict = external + "strict-fetch?case=" + Guid.NewGuid().ToString("N");
                string strictResult = await Fetch(core, strict, "strictResult");
                check(responses.ContainsKey(strict) && responses[strict] == 403 && strictResult != "\"200:EXTERNAL_OK\"",
                    "The production WebResourceRequested filter blocks an actual external fetch before the mock can replace its 403");

                string frame = external + "frame?case=" + Guid.NewGuid().ToString("N");
                await core.ExecuteScriptAsync("window.policyFrame=document.createElement('iframe');window.policyFrame.src=" + Json(frame) + ";document.body.appendChild(window.policyFrame);");
                await WaitFor(() => frames.ContainsKey(frame), "External iframe navigation decision");
                check(frames[frame] && (!responses.ContainsKey(frame) || responses[frame] == 403),
                    "The production frame-navigation guard cancels a real external iframe under the strict policy");

                await core.ExecuteScriptAsync("window.workerResult='pending';window.policyWorker=new Worker('/worker.js?case=" + Guid.NewGuid().ToString("N")
                    + "');window.policyWorker.onmessage=function(e){window.workerResult=e.data;};window.policyWorker.onerror=function(){window.workerResult='worker-error';};");
                string workerResult = await Result(core, "workerResult");
                check(responses.ContainsKey(workerTarget) && responses[workerTarget] == 403 && workerResult != "\"200:EXTERNAL_OK\"",
                    "A real dedicated worker's external fetch is intercepted and blocked by the production filter");

                window.SetExternalContentAllowed(true);
                string allowed = external + "allowed-fetch?case=" + Guid.NewGuid().ToString("N");
                string allowedResult = await Fetch(core, allowed, "allowedResult");
                check(responses.ContainsKey(allowed) && responses[allowed] == 200 && allowedResult == "\"200:EXTERNAL_OK\"",
                    "Explicitly allowing this NAS's external content permits a real CORS-enabled mocked fetch");

                window.SetExternalContentAllowed(false);
                string blockedAgain = external + "blocked-again?case=" + Guid.NewGuid().ToString("N");
                string blockedResult = await Fetch(core, blockedAgain, "blockedAgainResult");
                check(responses.ContainsKey(blockedAgain) && responses[blockedAgain] == 403 && blockedResult != "\"200:EXTERNAL_OK\""
                    && !WebContentPolicy.ReadExternalContentAllowed(settingsPath + ".security.json", window.BrowserProfileName),
                    "Turning compatibility off restores actual external-request blocking and persists the strict NAS policy");
                await core.ExecuteScriptAsync("if(window.policyWorker)window.policyWorker.terminate();if(window.policyFrame)window.policyFrame.remove();");
                check(unexpectedRequests == 0, "Content-policy browser checks use only synthetic fixture URLs and local mock responses");
            }
            finally
            {
                if (window != null)
                {
                    window.RequestExit();
                    window.Dispose();
                }
            }
        }

        private static CoreWebView2WebResourceResponse Response(CoreWebView2 core, string body, int status, string contentType)
        {
            return core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)), status,
                status == 200 ? "OK" : "Blocked fixture", "Content-Type: " + contentType + "; charset=utf-8\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store");
        }

        private static async Task<string> Fetch(CoreWebView2 core, string url, string key)
        {
            string target = "window[" + Json(key) + "]";
            await core.ExecuteScriptAsync(target + "='pending';fetch(" + Json(url) + ",{cache:'no-store'}).then(function(r){return r.text().then(function(t){"
                + target + "=r.status+':'+t;});}).catch(function(){" + target + "='fetch-error';});");
            return await Result(core, key);
        }

        private static async Task<string> Result(CoreWebView2 core, string key)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 10000)
            {
                string value = await core.ExecuteScriptAsync("window[" + Json(key) + "]");
                if (value != "null" && value != "\"pending\"") return value;
                await Task.Delay(40);
            }
            throw new TimeoutException("Content policy fixture result: " + key);
        }

        private static string Json(string value) { return new JavaScriptSerializer().Serialize(value); }

        private static async Task WaitFor(Func<bool> condition, string description, Func<string> diagnostic = null)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!condition())
            {
                if (timeout.ElapsedMilliseconds > 20000) throw new TimeoutException(description + (diagnostic == null ? String.Empty : diagnostic()));
                await Task.Delay(40);
            }
        }
    }
}
