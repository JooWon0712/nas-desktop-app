using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class PackageIconBrowserTests
    {
        private const string Home = "https://nas-icon-fixture.test:9443/";
        private const string IconPath = "webapi/entry.cgi?api=SYNO.Core.Synohdpack&version=1&method=getHDIcon&res=64&retina=true&path=webman/3rdparty/ExamplePackage/images/package_{0}.png";

        internal static async Task RunAsync(string output, Action<bool, string> check)
        {
            string directory = Path.Combine(Path.GetFullPath(output), "icons-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            string settingsPath = Path.Combine(directory, "connection.json");
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate(Home, ConnectionKind.DirectHttps, out settings)) throw new Exception("Invalid icon fixture.");
            settings.Save(settingsPath);
            byte[] png;
            using (Bitmap bitmap = new Bitmap(2, 2))
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(255, 23, 177, 61));
                bitmap.Save(stream, ImageFormat.Png);
                png = stream.ToArray();
            }
            MainWindow window = null;
            int unexpectedRequests = 0;
            bool templateObserved = false;
            Dictionary<string, int> statuses = new Dictionary<string, int>(StringComparer.Ordinal);
            Action<CoreWebView2> mock = delegate(CoreWebView2 view)
            {
                view.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                view.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    string value = e.Request.Uri;
                    if (value.Contains("package_{0}.png") || value.IndexOf("package_%7B0%7D.png", StringComparison.OrdinalIgnoreCase) >= 0)
                        templateObserved = true;
                    if (e.Response != null) return; // Never replace a production-policy block.
                    Uri uri;
                    if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https"
                        || (uri.Host != "nas-icon-fixture.test" && uri.Host != "external-icon-fixture.test"))
                    {
                        unexpectedRequests++;
                        e.Response = Reply(view, Encoding.UTF8.GetBytes("Unexpected fixture request"), 403, "text/plain");
                        return;
                    }
                    // Even a policy regression is answered locally; no fixture name is resolved.
                    e.Response = uri.AbsolutePath == "/webapi/entry.cgi"
                        ? Reply(view, png, 200, "image/png")
                        : Reply(view, Encoding.UTF8.GetBytes("<!doctype html><meta charset='utf-8'><title>Package icon fixture</title><body></body>"), 200, "text/html");
                };
                view.WebResourceResponseReceived += delegate(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
                {
                    statuses[e.Request.Uri] = e.Response.StatusCode;
                };
            };
            try
            {
                window = new MainWindow(settings, Path.Combine(directory, "profile"), true, settingsPath);
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "Icon fixture initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = window.Browser.CoreWebView2;
                mock(core);
                core.Navigate(Home);
                await WaitFor(() => core.Source == Home && core.DocumentTitle == "Package icon fixture", "Icon fixture document");
                string imageUrl = Home + IconPath + "&fixture=img";
                string image = await Decode(core, imageUrl, "iconImage");
                check(image == "\"2x2:23,177,61,255\"" && Status(statuses, imageUrl) == 200 && templateObserved,
                    "A real PNG package icon requested with a {0} query template decodes at the exact NAS HTTPS origin");

                string cssUrl = Home + IconPath + "&fixture=css";
                await core.ExecuteScriptAsync("window.iconBackground=document.createElement('div');iconBackground.id='package-background';iconBackground.style.cssText='width:32px;height:32px';iconBackground.style.backgroundImage='url(' + JSON.stringify(" + Json(cssUrl) + ") + ')';document.body.appendChild(iconBackground);");
                string cssImage = await Decode(core, cssUrl, "iconCss");
                check(cssImage == "\"2x2:23,177,61,255\"" && Status(statuses, cssUrl) == 200
                    && (await core.ExecuteScriptAsync("getComputedStyle(iconBackground).backgroundImage")).Contains("fixture=css"),
                    "A CSS background package icon retains its URL and loads the same valid PNG through the production filter");

                string externalUrl = "https://external-icon-fixture.test:9443/" + IconPath + "&fixture=external";
                string external = await Decode(core, externalUrl, "iconExternal");
                string otherPortUrl = "https://nas-icon-fixture.test:9444/" + IconPath + "&fixture=port";
                string otherPort = await Decode(core, otherPortUrl, "iconPort");
                check(external == "\"error\"" && otherPort == "\"error\""
                    && Status(statuses, externalUrl) == 403 && Status(statuses, otherPortUrl) == 403,
                    "Template-shaped image URLs at another host or port remain blocked and cannot decode");

                window.BrowserTabPrepared += delegate(MainWindow tab) { mock(tab.Browser.CoreWebView2); };
                await core.ExecuteScriptAsync("window.iconTab=window.open('/package-app','package_icon_fixture');void(0);");
                MainWindow child = null;
                await WaitFor(() => {
                    child = window.BrowserTabs.FirstOrDefault(item => !Object.ReferenceEquals(item, window) && item.Ready && !item.IsDisposed);
                    return child != null && child.Browser.CoreWebView2.DocumentTitle == "Package icon fixture";
                }, "Package icon application tab");
                string tabUrl = Home + IconPath + "&fixture=tab";
                string tabImage = await Decode(child.Browser.CoreWebView2, tabUrl, "iconTabImage");
                int blocksBefore = child.BlockedRequests;
                string tabBlocked = await Decode(child.Browser.CoreWebView2, externalUrl + "-tab", "iconTabExternal");
                // For a newly assigned popup, response metadata can omit this
                // event; decoding its known pixels still proves a usable image.
                check(tabImage == "\"2x2:23,177,61,255\""
                    && tabBlocked == "\"error\"" && child.BlockedRequests > blocksBefore && !child.TopLevel,
                    "A same-window NAS app tab decodes package icons while retaining external image blocking"
                    + " [image=" + tabImage + "; status=" + Status(statuses, tabUrl) + "; blockedImage=" + tabBlocked
                    + "; blockedDelta=" + (child.BlockedRequests - blocksBefore) + "; topLevel=" + child.TopLevel
                    + "; source=" + child.Browser.CoreWebView2.Source + "]");
                check(unexpectedRequests == 0, "Package icon checks use only isolated synthetic HTTPS documents and local PNG responses");
            }
            finally
            {
                if (window != null) { window.RequestExit(); window.Dispose(); }
            }
        }

        private static int Status(Dictionary<string, int> values, string url)
        {
            int result;
            if (values.TryGetValue(url, out result)) return result;
            // Runtime versions may escape the template before emitting response metadata.
            return values.TryGetValue(url.Replace("{", "%7B").Replace("}", "%7D"), out result) ? result : 0;
        }

        private static CoreWebView2WebResourceResponse Reply(CoreWebView2 core, byte[] body, int status, string contentType)
        {
            return core.Environment.CreateWebResourceResponse(new MemoryStream(body), status, status == 200 ? "OK" : "Blocked fixture",
                "Content-Type: " + contentType + "\r\nCache-Control: no-store");
        }

        private static async Task<string> Decode(CoreWebView2 core, string url, string key)
        {
            string target = "window[" + Json(key) + "]";
            await core.ExecuteScriptAsync(target + "='pending';(function(){var image=new Image();image.onload=function(){try{var canvas=document.createElement('canvas');canvas.width=2;canvas.height=2;var context=canvas.getContext('2d');context.drawImage(image,0,0);"
                + target + "=image.naturalWidth+'x'+image.naturalHeight+':'+Array.from(context.getImageData(0,0,1,1).data).join(',');}catch(e){" + target + "='decode-error';}};image.onerror=function(){" + target + "='error';};image.src=" + Json(url) + ";document.body.appendChild(image);})();");
            string result = null;
            await WaitFor(asyncCondition: async delegate {
                result = await core.ExecuteScriptAsync(target);
                return result != "null" && result != "\"pending\"";
            }, description: "PNG load result: " + key);
            return result;
        }

        private static string Json(string value) { return new JavaScriptSerializer().Serialize(value); }

        private static async Task WaitFor(Func<bool> condition, string description)
        {
            await WaitFor(delegate { return Task.FromResult(condition()); }, description);
        }

        private static async Task WaitFor(Func<Task<bool>> asyncCondition, string description)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!await asyncCondition())
            {
                if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(description);
                await Task.Delay(25);
            }
        }
    }
}
