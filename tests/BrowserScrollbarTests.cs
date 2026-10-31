using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class BrowserScrollbarTests
    {
        internal static async Task RunAsync(string output, string fixture, Action<bool, string> check)
        {
            string directory = Path.Combine(Path.GetFullPath(output), "scrollbar-browser-" + Guid.NewGuid().ToString("N"));
            string pages = Path.Combine(directory, "pages");
            Directory.CreateDirectory(pages);
            const string css = "html,body{margin:0;padding:0}body{height:2600px}" +
                "::-webkit-scrollbar{width:0px!important;height:0px!important;display:none!important}" +
                "#nested{overflow:auto;position:relative;width:320px;height:180px}" +
                "#content{width:680px;height:900px}iframe{width:400px;height:220px;border:0}";
            string body = "<div id='nested'><div id='content'>Synthetic overflow content</div></div>";
            File.WriteAllText(Path.Combine(pages, "index.html"), "<!doctype html><meta charset='utf-8'><title>Scrollbar fixture</title><style>" +
                css + "</style>" + body + "<iframe id='child' src='child.html'></iframe>");
            File.WriteAllText(Path.Combine(pages, "child.html"), "<!doctype html><meta charset='utf-8'><title>Scrollbar child fixture</title><style>" +
                css + "</style>" + body);
            ConnectionSettings settings;
            ConnectionSettings.TryCreate("https://scrollbar-fixture.test/index.html", ConnectionKind.DirectHttps, out settings);
            MainWindow window = null;
            try
            {
                window = new MainWindow(settings, Path.Combine(directory, "profile"), true, Path.Combine(directory, "connection.json"));
                window.Show();
                await WaitFor(() => window.Ready || window.InitializationError != null, "Scrollbar WebView initialization");
                if (window.InitializationError != null) throw window.InitializationError;
                CoreWebView2 core = window.Browser.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("scrollbar-fixture.test", pages, CoreWebView2HostResourceAccessKind.DenyCors);
                core.Navigate(settings.Url);
                await WaitFor(() => core.DocumentTitle == "Scrollbar fixture", "Scrollbar fixture load");
                check(await IsTrue(core, @"(function(){var d=document.documentElement,n=document.getElementById('nested');
                    var a=getComputedStyle(d,'::-webkit-scrollbar'),b=getComputedStyle(n,'::-webkit-scrollbar');
                    return a.width==='14px'&&a.display==='block'&&b.width==='14px'&&b.height==='14px'&&b.display==='block';}())"),
                    "Document-created scrollbar styling makes hidden document and nested native scrollbars visible");
                using (FileStream preview = File.Create(Path.Combine(directory, "visible-native-scrollbars.png")))
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);

                string layout = await core.ExecuteScriptAsync(@"(function(){var n=document.getElementById('nested'),s=getComputedStyle(n);
                    return {overflowX:s.overflowX,overflowY:s.overflowY,width:s.width,height:s.height,position:s.position,
                        offsetWidth:n.offsetWidth,offsetHeight:n.offsetHeight,scrollHeight:n.scrollHeight,clientHeight:n.clientHeight,
                        scrollWidth:n.scrollWidth,clientWidth:n.clientWidth,documentHeight:document.scrollingElement.scrollHeight,innerHeight:innerHeight};}())");
                // Classic scrollbars consume the content box: computed width/height
                // become 306/166 while the fixture's outer size stays 320/180.
                check(await IsTrue(core, @"(function(){var n=document.getElementById('nested'),s=getComputedStyle(n);
                    return s.overflowX==='auto'&&s.overflowY==='auto'&&n.offsetWidth===320&&n.offsetHeight===180&&s.position==='relative'
                    &&n.scrollHeight>n.clientHeight&&n.scrollWidth>n.clientWidth&&document.scrollingElement.scrollHeight>innerHeight;}())"),
                    "Scrollbar styling preserves NAS overflow, element sizing, positioning and real scroll ranges [" + layout + "]");

                await core.ExecuteScriptAsync("window.scrollTo(0,320);var n=document.getElementById('nested');n.scrollTop=160;n.scrollLeft=120;");
                check(await IsTrue(core, "window.scrollY>0&&document.getElementById('nested').scrollTop===160&&document.getElementById('nested').scrollLeft===120"),
                    "Document and nested content still scroll vertically and horizontally with the visible native bars");

                // Remove only the application's CSS in this controlled fixture to
                // verify the hidden baseline, then exercise a safe repeated install.
                await core.ExecuteScriptAsync("document.getElementById('" + BrowserScrollbarStyle.StyleId + "').remove();");
                check(await IsTrue(core, "getComputedStyle(document.getElementById('nested'),'::-webkit-scrollbar').width==='0px'&&getComputedStyle(document.getElementById('nested'),'::-webkit-scrollbar').display==='none'"),
                    "The controlled NAS fixture really hides its native scrollbar before the app styling is restored");
                await core.ExecuteScriptAsync(BrowserScrollbarStyle.Script);
                await core.ExecuteScriptAsync(BrowserScrollbarStyle.Script);
                check(await IsTrue(core, "getComputedStyle(document.getElementById('nested'),'::-webkit-scrollbar').width==='14px'&&document.querySelectorAll('#" + BrowserScrollbarStyle.StyleId + "').length===1&&document.getElementById('nested').scrollTop===160"),
                    "Reapplying native scrollbar visibility restores its width without duplicating styles or resetting scroll position");

                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                bool childReady = false;
                while (!childReady && DateTime.UtcNow < deadline)
                {
                    childReady = await IsTrue(core, @"(function(){var f=document.getElementById('child'),d=f.contentDocument;
                        return d&&d.title==='Scrollbar child fixture'&&d.readyState==='complete';}())");
                    if (!childReady) await Task.Delay(50);
                }
                check(childReady && await IsTrue(core, @"(function(){var w=document.getElementById('child').contentWindow,n=w.document.getElementById('nested');
                    n.scrollTop=90;return w.getComputedStyle(n,'::-webkit-scrollbar').width==='14px'&&w.getComputedStyle(n,'::-webkit-scrollbar').display==='block'
                    &&w.getComputedStyle(n).overflowY==='auto'&&n.scrollTop===90;}())"),
                    "Same-origin child frames receive native scrollbar visibility while retaining their own scrolling");
            }
            finally
            {
                if (window != null) { window.RequestExit(); window.Dispose(); }
            }
        }

        private static async Task<bool> IsTrue(CoreWebView2 core, string script)
        {
            return await core.ExecuteScriptAsync(script) == "true";
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
