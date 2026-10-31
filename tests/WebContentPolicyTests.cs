using System;
using System.IO;
using System.Threading.Tasks;

namespace NasDesk
{
    internal static class WebContentPolicyTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            using (ConnectionSession direct = new ConnectionSession("https://nas.content.test:9443/ui/", ConnectionKind.DirectHttps))
            {
                check(WebContentPolicy.AllowsRequest(direct, "https://NAS.CONTENT.TEST:9443/api/status?view=files", false, false)
                    && WebContentPolicy.AllowsRequest(direct, "wss://nas.content.test:9443/events", false, false),
                    "Strict content policy allows HTTPS and secure WebSocket URLs only at the configured NAS origin");
                check(!WebContentPolicy.AllowsRequest(direct, "https://nas.content.test/api/status", false, false)
                    && !WebContentPolicy.AllowsRequest(direct, "https://other.content.test:9443/", false, false)
                    && !WebContentPolicy.AllowsRequest(direct, "https://nas.content.test.attacker.invalid:9443/", false, false)
                    && !WebContentPolicy.AllowsRequest(direct, "wss://other.content.test:9443/events", false, false),
                    "Strict content policy rejects different hosts, effective ports and lookalike suffixes");
                check(!WebContentPolicy.AllowsRequest(direct, "https://external.content.test/", false, true),
                    "A bootstrap flag cannot relax a direct NAS connection's external-content policy");
                check(WebContentPolicy.AllowsRequest(direct, "https://external.content.test/app.js", true, false)
                    && WebContentPolicy.AllowsRequest(direct, "wss://external.content.test/events", true, false),
                    "A deliberate per-NAS compatibility choice permits external HTTPS and WSS requests");
                check(WebContentPolicy.AllowsRequest(direct, "about:blank", false, false)
                    && WebContentPolicy.AllowsRequest(direct, "data:image/svg+xml,%3Csvg/%3E", false, false)
                    && WebContentPolicy.AllowsRequest(direct, "blob:https://nas.content.test:9443/local-fixture", false, false),
                    "Local blank, data and blob resources remain usable under the strict content policy");

                bool unsafeBlocked = true;
                foreach (string value in new[] { "http://nas.content.test:9443/", "ws://nas.content.test:9443/events",
                    "file:///C:/synthetic-fixture.txt", "ftp://external.content.test/", "ms-settings:privacy",
                    "javascript:alert(1)", "custom-nas://fixture", "relative/path", String.Empty, null })
                    unsafeBlocked &= !WebContentPolicy.AllowsRequest(direct, value, false, false)
                        && !WebContentPolicy.AllowsRequest(direct, value, true, true);
                check(unsafeBlocked, "External-content compatibility never enables HTTP, file, custom schemes or malformed requests");
            }

            using (ConnectionSession quickConnect = new ConnectionSession("content-demo", ConnectionKind.SynologyQuickConnect,
                (value, cancellation) => Task.FromResult(value == "https://resolved.content.test:5001/")))
            {
                check(!WebContentPolicy.AllowsRequest(quickConnect, "https://cdn.content.test/script.js", false, false)
                    && WebContentPolicy.AllowsRequest(quickConnect, "https://cdn.content.test/script.js", false, true)
                    && WebContentPolicy.AllowsRequest(quickConnect, "https://content-demo.quickconnect.to/", false, true),
                    "QuickConnect HTTPS compatibility is limited to the caller's selected bootstrap-document stage");
                check(!WebContentPolicy.AllowsRequest(quickConnect, "wss://cdn.content.test/events", false, true)
                    && !WebContentPolicy.AllowsRequest(quickConnect, "http://cdn.content.test/script.js", false, true),
                    "QuickConnect bootstrap compatibility does not enable external WSS or insecure HTTP requests");
                bool verified = quickConnect.VerifyAndAllowAsync("https://resolved.content.test:5001/").GetAwaiter().GetResult();
                check(verified && WebContentPolicy.AllowsRequest(quickConnect, "https://resolved.content.test:5001/api/", false, false)
                    && WebContentPolicy.AllowsRequest(quickConnect, "wss://resolved.content.test:5001/events", false, false)
                    && !WebContentPolicy.AllowsRequest(quickConnect, "https://resolved.content.test:5002/", false, false)
                    && !WebContentPolicy.AllowsRequest(quickConnect, "https://cdn.content.test/script.js", false, false),
                    "After bootstrap, only verified NAS HTTPS and WSS origins retain strict access");
            }

            string directory = Path.Combine(Path.GetFullPath(output), "web-content-policy-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "security.json");
            string first = new String('a', 64), second = new String('b', 64);
            check(!WebContentPolicy.ReadExternalContentAllowed(path, first) && !File.Exists(path),
                "External content defaults to blocked without creating a policy file");
            WebContentPolicy.SaveExternalContentAllowed(path, first, true);
            check(WebContentPolicy.ReadExternalContentAllowed(path, first) && !WebContentPolicy.ReadExternalContentAllowed(path, second),
                "An external-content exception belongs only to the selected NAS profile hash");
            WebContentPolicy.SaveExternalContentAllowed(path, second, true);
            WebContentPolicy.SaveExternalContentAllowed(path, first, false);
            check(!WebContentPolicy.ReadExternalContentAllowed(path, first) && WebContentPolicy.ReadExternalContentAllowed(path, second),
                "Saving one NAS policy preserves another NAS's compatibility choice");

            bool malformedBlocked = true;
            foreach (string malformed in new[] { "{invalid", "[]", "null", "{}", "{\"" + first + "\":null}",
                "{\"" + first + "\":\"true\"}", "{\"" + first + "\":1}", "{\"" + second + "\":true}" })
            {
                File.WriteAllText(path, malformed);
                malformedBlocked &= !WebContentPolicy.ReadExternalContentAllowed(path, first)
                    && File.ReadAllText(path) == malformed;
            }
            check(malformedBlocked, "Malformed or nonboolean policy data fails closed without silently granting or rewriting an exception");

            bool malformedSavePreserved = true;
            foreach (string malformed in new[] { "{invalid", "null", "{\"" + first + "\":\"true\"}", "{\"" + first + "\":1}" })
            {
                File.WriteAllText(path, malformed);
                bool rejected = false;
                try { WebContentPolicy.SaveExternalContentAllowed(path, first, true); }
                catch (InvalidDataException) { rejected = true; }
                malformedSavePreserved &= rejected && File.ReadAllText(path) == malformed;
            }
            check(malformedSavePreserved, "Policy saves report malformed data and preserve the original file instead of silently replacing its permissions");

            File.WriteAllText(path, "{\"" + first + "\":false,\"" + second + "\":true}");
            string beforeFailure = File.ReadAllText(path);
            bool saveFailed = false;
            using (FileStream held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { WebContentPolicy.SaveExternalContentAllowed(path, first, true); }
                catch (IOException) { saveFailed = true; }
                catch (UnauthorizedAccessException) { saveFailed = true; }
            }
            check(saveFailed && File.ReadAllText(path) == beforeFailure && Directory.GetFiles(directory, "security.json.*.tmp").Length == 0,
                "A failed policy replacement preserves the old permissions and cleans the temporary file");
        }
    }
}
