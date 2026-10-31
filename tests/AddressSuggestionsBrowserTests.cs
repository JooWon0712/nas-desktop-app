using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NasDesk
{
    internal static class AddressSuggestionsBrowserTests
    {
        internal static async Task RunAsync(MainWindow window, CoreWebView2 core, string settingsPath, Action<bool, string> check)
        {
            AddressBar address = window.Address;
            ConnectionSettings original = ConnectionSettings.Read(settingsPath);
            string savedSettings = File.ReadAllText(settingsPath);
            string source = core.Source;
            string cookies = await core.ExecuteScriptAsync("document.cookie");
            string storage = await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')");
            string profile = core.Environment.UserDataFolder;
            string historyPath = settingsPath + ".history.json";
            AddressHistoryStore store = new AddressHistoryStore(historyPath);
            ConnectionSettings named;
            ConnectionSettings.TryCreate("https://named-picker.test:8443/ui/", ConnectionKind.DirectHttps, out named);
            store.Add(named, "집 NAS");
            string savedHistory = File.ReadAllText(historyPath);
            address.SetAddress(original.Url, original.Kind);
            window.ShowAddressSuggestions();
            check(window.AddressSuggestions == null && !address.IsEditing,
                "A hidden or locked address cannot display the history picker");

            window.OpenAddressEditor();
            ClickInput(address.Input);
            AddressHistoryPopup first = window.AddressSuggestions;
            check(first != null && first.Visible && address.Input.Focused && !address.Input.ReadOnly
                && File.ReadAllText(settingsPath) == savedSettings && core.Source == source
                && File.ReadAllText(historyPath) == savedHistory,
                "Clicking the editable address opens the named history picker without stealing focus, writing or navigating");
            check(!first.HandleEditorKey(Keys.Enter),
                "An unselected history picker does not consume Enter or replace the typed address");
            check(Command(address, Keys.Down) && Command(address, Keys.Enter)
                && window.AddressSuggestions == null && address.IsEditing && address.Input.Focused
                && address.AddressText == named.Url && address.Kind == named.Kind
                && core.Source == source && File.ReadAllText(settingsPath) == savedSettings
                && File.ReadAllText(historyPath) == savedHistory,
                "Down and Enter select a named NAS into the editable draft without connecting or changing its saved name");
            Command(address, Keys.Escape);

            window.OpenAddressEditor();
            ClickInput(address.Input);
            address.AddressText = "https://typed-picker.test/";
            check(window.AddressSuggestions == null && address.IsEditing && address.Input.Focused
                && address.AddressText == "https://typed-picker.test/" && core.Source == source,
                "Typing closes the history picker while preserving normal address editing");
            ClickInput(address.Input);
            bool firstEscape = Command(address, Keys.Escape);
            check(firstEscape && window.AddressSuggestions == null && address.IsEditing
                && address.AddressText == "https://typed-picker.test/" && core.Source == source,
                "The first Escape dismisses only the open history picker and preserves the address draft");
            check(Command(address, Keys.Escape) && !address.IsEditing && address.AddressText == original.Url
                && File.ReadAllText(settingsPath) == savedSettings && File.ReadAllText(historyPath) == savedHistory,
                "A second Escape cancels the address draft without changing settings or named history");

            window.OpenAddressEditor();
            ClickInput(address.Input);
            address.Kind = address.Kind == ConnectionKind.DirectHttps ? ConnectionKind.SynologyQuickConnect : ConnectionKind.DirectHttps;
            check(window.AddressSuggestions == null && address.IsEditing,
                "Changing connection type dismisses the old history picker without closing the editor");
            Command(address, Keys.Escape);
            window.OpenAddressEditor();
            ClickInput(address.Input);
            window.Browser.Focus();
            await WaitFor(() => window.AddressSuggestions == null, "Native browser focus dismisses history picker");
            check(address.IsEditing && core.Source == source && File.ReadAllText(settingsPath) == savedSettings,
                "Moving focus into the live native WebView dismisses suggestions without cancelling the address draft");
            window.OpenAddressEditor();
            ClickInput(address.Input);
            System.Drawing.Size originalSize = window.Size;
            window.Width = originalSize.Width - 10;
            check(window.AddressSuggestions == null && address.IsEditing && core.Source == source,
                "Resizing the NAS window dismisses an anchored picker without losing the address draft");
            window.Size = originalSize;
            window.OpenAddressEditor();
            ClickInput(address.Input);
            AddressHistoryPopup beforeHide = window.AddressSuggestions;
            window.HideToTray();
            check(window.AddressSuggestions == null && beforeHide.IsDisposed && !window.Visible && !window.IsDisposed,
                "Hiding the NAS window dismisses and disposes its transient address history picker");
            window.RestoreFromTray();
            check(window.Visible && address.IsEditing && address.Input.Visible
                && Object.ReferenceEquals(core, window.Browser.CoreWebView2) && core.Source == source
                && core.Environment.UserDataFolder == profile && File.ReadAllText(settingsPath) == savedSettings
                && await core.ExecuteScriptAsync("document.cookie") == cookies
                && await core.ExecuteScriptAsync("localStorage.getItem('nas_desk_test')") == storage,
                "Picker interaction and tray restoration retain the NAS page, live profile, cookies and local storage");
            Command(address, Keys.Escape);

            store.Add(original, "현재 NAS");
            window.OpenAddressEditor();
            bool completed = false;
            EventHandler<CoreWebView2NavigationCompletedEventArgs> onCompleted = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.IsSuccess && core.Source == original.Url) completed = true;
            };
            core.NavigationCompleted += onCompleted;
            try
            {
                address.ConnectButton.PerformClick();
                await WaitFor(() => completed && !address.IsEditing, "Named current NAS reconnection");
                check(store.ReadEntries().First().Name == "현재 NAS" && store.ReadEntries().First().Url == original.Url
                    && File.ReadAllText(settingsPath) == savedSettings,
                    "Connecting through the address editor records recency without erasing an existing NAS name");
            }
            finally { core.NavigationCompleted -= onCompleted; }
        }

        private static void ClickInput(AddressInput input)
        {
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(input, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0) });
        }

        private static bool Command(AddressBar address, Keys keys)
        {
            Message message = Message.Create(address.Input.Handle, 0x0100, (IntPtr)keys, IntPtr.Zero);
            return address.PreProcessMessage(ref message);
        }

        private static async Task WaitFor(Func<bool> ready, string description)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!ready())
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException(description);
                await Task.Delay(50);
            }
        }
    }
}
