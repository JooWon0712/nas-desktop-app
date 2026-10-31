using System;
using System.IO;

namespace NasDesk
{
    internal static class ConnectionSettingsTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string directory = Path.Combine(output, "generic-connection-settings");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "connection.json");
            string missing = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
            ConnectionSettings empty = ConnectionSettings.Read(missing);
            check(empty.Kind == ConnectionKind.DirectHttps && empty.Type == null && empty.Url == null,
                "A fresh installation defaults to direct NAS HTTPS without a stored legacy mode");

            File.WriteAllText(path, "{\"Url\":\"my-nas\"}");
            string original = File.ReadAllText(path);
            ConnectionSettings legacyQuickConnect = ConnectionSettings.Read(path);
            check(legacyQuickConnect.Kind == ConnectionKind.SynologyQuickConnect
                && legacyQuickConnect.Type == "quickconnect" && legacyQuickConnect.Url == "https://my-nas.quickconnect.to/"
                && File.ReadAllText(path) == original,
                "Legacy QuickConnect settings gain an explicit in-memory type without changing the original file");
            File.WriteAllText(path, "{\"Url\":\"https://nas.example:8443/ui/\"}");
            ConnectionSettings legacyDirect = ConnectionSettings.Read(path);
            check(legacyDirect.Kind == ConnectionKind.DirectHttps && legacyDirect.Type == "https"
                && legacyDirect.Url == "https://nas.example:8443/ui/",
                "Legacy direct NAS settings keep their HTTPS origin and become explicitly direct");
            File.WriteAllText(path, "{\"Url\":\"https://device-alias.us2.quickconnect.to/\"}");
            check(ConnectionSettings.Read(path).Kind == ConnectionKind.DirectHttps,
                "Legacy resolved QuickConnect endpoints retain direct exact-origin behavior");

            ConnectionSettings direct;
            check(ConnectionSettings.TryCreate("nas:9443/ui/", ConnectionKind.DirectHttps, out direct)
                && direct.Kind == ConnectionKind.DirectHttps && direct.Url == "https://nas:9443/ui/" && direct.Type == "https",
                "A new direct NAS name is stored as HTTPS rather than a Synology ID");
            direct.Save(path);
            ConnectionSettings reopened = ConnectionSettings.Read(path);
            check(reopened.Kind == ConnectionKind.DirectHttps && reopened.Url == direct.Url
                && !File.Exists(path + ".tmp"),
                "Explicit connection type and address survive atomic replacement and reopening");

            ConnectionSettings portal;
            ConnectionSettings.TryCreate("https://demo-nas.quickconnect.to/", ConnectionKind.DirectHttps, out portal);
            portal.Save(path);
            check(ConnectionSettings.Read(path).Kind == ConnectionKind.DirectHttps,
                "An explicit direct QuickConnect-looking host remains direct after saving and reopening");
            ConnectionSettings synology;
            ConnectionSettings.TryCreate("quickconnect.to/demo-nas", ConnectionKind.SynologyQuickConnect, out synology);
            synology.Save(path);
            check(ConnectionSettings.Read(path).Kind == ConnectionKind.SynologyQuickConnect,
                "Explicit Synology connection type survives saving and reopening");
            File.WriteAllText(path, "{\"Url\":\"nas.local:9443/\",\"Type\":\"HTTPS\"}");
            check(ConnectionSettings.Read(path).Type == "https", "Known persisted connection types are case-insensitive and canonicalized");

            File.WriteAllText(path, "{\"Url\":\"my-nas\",\"Type\":\"unknown-provider\"}");
            bool rejected = false;
            try { ConnectionSettings.Read(path); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "An unknown stored connection type is not reinterpreted through the legacy QuickConnect parser");

            direct.Save(path);
            string valid = File.ReadAllText(path);
            rejected = false;
            try { new ConnectionSettings { Url = "my-nas", Type = "unknown-provider" }.Save(path); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(path) == valid,
                "Invalid connection settings cannot overwrite a valid saved NAS connection");
            new ConnectionSettings { Url = "demo-nas" }.Save(path);
            check(ConnectionSettings.Read(path).Kind == ConnectionKind.SynologyQuickConnect,
                "Legacy in-memory connection objects retain their previous interpretation when saved");
            check(!ConnectionSettings.IsValid("https://@nas.local/") && !ConnectionSettings.IsValid("nas.local")
                && !ConnectionSettings.IsInternal("https://@nas.local/", "https://nas.local/")
                && !ConnectionSettings.IsInternal(null, "https://nas.local/"),
                "URL and internal-navigation checks reject empty userinfo and non-absolute input");
        }
    }
}
