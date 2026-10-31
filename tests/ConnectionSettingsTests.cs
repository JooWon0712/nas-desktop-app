using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

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

            RunProtectedSettingsTests(directory, check);
        }

        private static void RunProtectedSettingsTests(string directory, Action<bool, string> check)
        {
            string path = Path.Combine(directory, "protected-connection.json");
            ConnectionSettings connection;
            if (!ConnectionSettings.TryCreate("https://token-settings.test/ui/?access_token=settings-fixture-secret#/login?code=fragment-fixture-secret",
                ConnectionKind.DirectHttps, out connection)) throw new Exception("Invalid fixture.");
            connection.Save(path);
            string stored = File.ReadAllText(path);
            IDictionary<string, object> envelope = (IDictionary<string, object>)new JavaScriptSerializer().DeserializeObject(stored);
            check(envelope.Count == 3 && (string)envelope["Protection"] == "DPAPI.CurrentUser" && (int)envelope["Version"] == 1
                && !stored.Contains("token-settings.test") && !stored.Contains("settings-fixture-secret")
                && !stored.Contains("fragment-fixture-secret") && ConnectionSettings.Read(path).Url == connection.Url
                && ConnectionSettings.Read(path).Kind == ConnectionKind.DirectHttps,
                "Current connection protects complete query and fragment token URLs without changing the reconnect address");
            check(!ConnectionSettings.ProtectIfNeeded(path) && File.ReadAllText(path) == stored,
                "Protected connection startup migration validates but does not rewrite existing ciphertext");

            string legacy = "{\"Url\":\"https://legacy-settings.test/ui/?sid=legacy-fixture-secret#/signin\",\"Type\":\"https\"}";
            File.WriteAllText(path, legacy);
            string legacyUrl = ConnectionSettings.Read(path).Url;
            check(File.ReadAllText(path) == legacy && ConnectionSettings.ProtectIfNeeded(path)
                && ConnectionSettings.Read(path).Url == legacyUrl && !File.ReadAllText(path).Contains("legacy-fixture-secret"),
                "Explicit connection migration protects legacy plaintext while preserving its full address");

            stored = File.ReadAllText(path);
            envelope = (IDictionary<string, object>)new JavaScriptSerializer().DeserializeObject(stored);
            byte[] encrypted = Convert.FromBase64String((string)envelope["Payload"]);
            encrypted[encrypted.Length - 1] ^= 1;
            envelope["Payload"] = Convert.ToBase64String(encrypted);
            string tampered = new JavaScriptSerializer().Serialize(envelope);
            File.WriteAllText(path, tampered);
            bool rejectedRead = false, rejectedMigration = false;
            try { ConnectionSettings.Read(path); }
            catch (InvalidDataException) { rejectedRead = true; }
            try { ConnectionSettings.ProtectIfNeeded(path); }
            catch (InvalidDataException) { rejectedMigration = true; }
            check(rejectedRead && rejectedMigration && File.ReadAllText(path) == tampered,
                "Tampered protected connection data is reported and never silently reset by migration");

            bool malformedPreserved = true;
            foreach (string malformed in new[] { "{bad", "{\"Protection\":\"DPAPI.CurrentUser\",\"Version\":2,\"Payload\":\"bad\"}",
                "{\"Protection\":\"unknown\",\"Version\":1,\"Payload\":\"bad\"}", "{\"Protection\":\"DPAPI.CurrentUser\",\"Version\":1,\"Payload\":\"not-base64\"}" })
            {
                File.WriteAllText(path, malformed);
                bool rejected = false;
                try { ConnectionSettings.ProtectIfNeeded(path); }
                catch (InvalidDataException) { rejected = true; }
                malformedPreserved &= rejected && File.ReadAllText(path) == malformed;
            }
            check(malformedPreserved, "Malformed or unsupported protection envelopes cannot overwrite a saved settings file during migration");

            File.WriteAllText(path, legacy);
            bool migrationFailed = false;
            using (FileStream held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { ConnectionSettings.ProtectIfNeeded(path); }
                catch (IOException) { migrationFailed = true; }
                catch (UnauthorizedAccessException) { migrationFailed = true; }
            }
            check(migrationFailed && File.ReadAllText(path) == legacy && Directory.GetFiles(directory, "protected-connection.json.*.tmp").Length == 0,
                "A failed connection protection migration keeps the original and leaves no temporary file");

            connection.Url = "HTTPS://TOKEN-SETTINGS.TEST:443/ui/?access_token=preserved-fixture-secret";
            string uncommittedUrl = connection.Url;
            bool saveFailed = false;
            using (FileStream held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { connection.Save(path); }
                catch (IOException) { saveFailed = true; }
                catch (UnauthorizedAccessException) { saveFailed = true; }
            }
            check(saveFailed && connection.Url == uncommittedUrl && File.ReadAllText(path) == legacy
                && Directory.GetFiles(directory, "protected-connection.json.*.tmp").Length == 0,
                "Failed protected connection saves do not mutate the caller's URL or replace the current settings");

            string missing = Path.Combine(directory, "missing-protected-connection.json");
            check(!ConnectionSettings.ProtectIfNeeded(missing) && !File.Exists(missing),
                "Protecting missing connection settings does not create an address file");
        }
    }
}
