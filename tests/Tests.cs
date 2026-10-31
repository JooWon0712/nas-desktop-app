using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class Tests
    {
        private static readonly List<string> results = new List<string>();
        private static string output;
        private static string fixture;
        private static int exitCode = 1;

        [STAThread]
        private static int Main(string[] args)
        {
            output = args[0]; fixture = args[1];
            Directory.CreateDirectory(output);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UnitTests();
            using (MainWindow window = new MainWindow(new ConnectionSettings { Url = "https://nas-desk.test/" }, Path.Combine(output, "profile"), true))
            {
                window.Shown += async delegate
                {
                    try
                    {
                        await RunBrowserTests(window);
                        exitCode = 0;
                    }
                    catch (Exception error) { results.Add("FAIL " + error); }
                    finally
                    {
                        window.RequestExit();
                        File.WriteAllLines(Path.Combine(output, "results.txt"), results);
                    }
                };
                Application.Run(window);
            }
            foreach (string result in results) Console.WriteLine(result);
            return exitCode;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            results.Add("PASS " + name);
        }

        private static void UnitTests()
        {
            Check(ConnectionSettings.IsValid("https://nas.example:5001/"), "HTTPS address accepted");
            Check(!ConnectionSettings.IsValid("http://nas.example/") && !ConnectionSettings.IsValid("https://user:secret@nas.example/"), "HTTP and credentials in URL rejected");
            Check(ConnectionSettings.IsInternal("https://nas.example:5001/webman/", "https://nas.example:5001/"), "DSM navigation accepted");
            Check(!ConnectionSettings.IsInternal("https://nas.example.attacker.invalid:5001/", "https://nas.example:5001/"), "Lookalike host rejected");
            Check(!ConnectionSettings.IsInternal("https://nas.example:5002/", "https://nas.example:5001/"), "Different origin requires external navigation");
            Check(MainWindow.ShouldHide(CloseReason.UserClosing, false, true, false), "X hides primary window");
            Check(!MainWindow.ShouldHide(CloseReason.WindowsShutDown, false, true, false)
                && !MainWindow.ShouldHide(CloseReason.TaskManagerClosing, false, true, false)
                && !MainWindow.ShouldHide(CloseReason.UserClosing, true, true, false)
                && !MainWindow.ShouldHide(CloseReason.UserClosing, false, true, true)
                && !MainWindow.ShouldHide(CloseReason.UserClosing, false, false, false), "OS shutdown, task manager, explicit exit, popups and missing tray do not hide");
            string settingsFile = Path.Combine(output, "connection.json");
            new ConnectionSettings { Url = "https://nas.example:5001/" }.Save(settingsFile);
            new ConnectionSettings { Url = "https://nas.example:5002/" }.Save(settingsFile);
            Check(ConnectionSettings.Read(settingsFile).Url == "https://nas.example:5002/", "Connection settings saved and replaced atomically");
        }

        private static async Task WaitFor(Func<bool> condition, int milliseconds, string description)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition())
            {
                if (DateTime.UtcNow > end) throw new TimeoutException(description);
                await Task.Delay(100);
            }
        }

        private static async Task RunBrowserTests(MainWindow window)
        {
            await WaitFor(() => window.Ready || window.InitializationError != null, 20000, "WebView2 initialization");
            if (window.InitializationError != null) throw window.InitializationError;
            Check(window.Ready && window.TrayVisible, "WebView2 and notification icon initialized");
            CoreWebView2 core = window.Browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("nas-desk.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Navigate("https://nas-desk.test/index.html");
            await WaitFor(() => core.DocumentTitle == "NAS Desk local fixture", 10000, "Local HTTPS fixture");
            Check(!core.Settings.AreHostObjectsAllowed && !core.Settings.IsWebMessageEnabled, "Native host bridge disabled");
            Check((await core.ExecuteScriptAsync("document.cookie")).Contains("nas_desk_test=persisted"), "HTTPS cookies work");
            await core.ExecuteScriptAsync("window.open('about:blank', 'nas_desk_popup')");
            await WaitFor(() => Application.OpenForms.Count >= 2, 10000, "Same-profile popup");
            MainWindow popup = Application.OpenForms.Cast<Form>().OfType<MainWindow>().First(w => w != window);
            await WaitFor(() => popup.Ready, 10000, "Popup document");
            Check((await popup.Browser.CoreWebView2.ExecuteScriptAsync("document.cookie")).Contains("nas_desk_test=persisted"), "Popup shares NAS session");
            int before = Int32.Parse(await core.ExecuteScriptAsync("window.ticks"));
            window.Close();
            Check(!window.Visible && !window.IsDisposed && window.TrayVisible, "Real Close hides without disposing WebView2");
            Check(!popup.Visible && !popup.IsDisposed, "Popup also hides with primary window");
            await Task.Delay(2500);
            Check(Int32.Parse(await core.ExecuteScriptAsync("window.ticks")) > before, "Web scripts continue while hidden");
            window.RestoreFromTray();
            Check(window.Visible && popup.Visible && Object.ReferenceEquals(core, window.Browser.CoreWebView2), "Restore retains primary and popup WebView2 instances");
            Check((await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')")) == "\"persisted\"", "Web storage retained after hide/restore");
            await popup.Browser.CoreWebView2.ExecuteScriptAsync("window.close()");
            await WaitFor(() => popup.IsDisposed, 5000, "window.close closes popup");
            Check(popup.IsDisposed && !window.IsDisposed, "Web popup close leaves primary window running");
            bool blocked = false;
            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { if (e.Uri.StartsWith("https://blocked.invalid/")) blocked = e.Cancel; };
            core.Navigate("https://blocked.invalid/");
            await WaitFor(() => blocked, 5000, "External navigation blocked before request");
            Check(blocked, "Unconfigured origin blocked");
            window.RequestExit();
            Check(window.IsDisposed && popup.IsDisposed && !window.TrayVisible, "Explicit exit disposes windows and tray");
        }
    }
}
