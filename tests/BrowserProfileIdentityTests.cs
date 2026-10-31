using System;
using System.Globalization;

namespace NasDesk
{
    internal static class BrowserProfileIdentityTests
    {
        internal static void Run(Action<bool, string> check)
        {
            string direct = Identity("https://nas.profile.test/", ConnectionKind.DirectHttps);
            bool safeName = direct.Length == 64;
            foreach (char character in direct) safeName &= character >= '0' && character <= '9' || character >= 'a' && character <= 'f';
            check(safeName && direct.IndexOf("nas", StringComparison.Ordinal) < 0,
                "Browser profile names are opaque sixty-four-character lowercase SHA-256 hex values");
            check(direct == Identity("HTTPS://NAS.PROFILE.TEST:443/ui/?token=fixture-secret#/login", ConnectionKind.DirectHttps)
                && direct == Identity("nas.profile.test/other/path", ConnectionKind.DirectHttps),
                "Direct HTTPS profiles depend on canonical origin rather than paths, queries, fragments or spelling");
            check(direct != Identity("https://nas.profile.test:9443/", ConnectionKind.DirectHttps)
                && direct != Identity("https://other.profile.test/", ConnectionKind.DirectHttps),
                "Different NAS hosts and effective HTTPS ports receive separate browser profiles");
            check(Identity("https://bücher.profile.test/", ConnectionKind.DirectHttps)
                == Identity("https://xn--bcher-kva.profile.test:443/ui/", ConnectionKind.DirectHttps),
                "Unicode and ASCII IDN spellings resolve to the same direct browser profile");
            check(Identity("https://[2001:db8::10]:443/", ConnectionKind.DirectHttps)
                == Identity("https://[2001:0db8:0:0:0:0:0:10]/ui/", ConnectionKind.DirectHttps),
                "Equivalent canonical IPv6 NAS origins receive the same browser profile");

            string quickConnect = Identity("Demo-Nas", ConnectionKind.SynologyQuickConnect);
            check(quickConnect == Identity("https://quickconnect.to/demo-nas/file/?token=fixture-secret#/login", ConnectionKind.SynologyQuickConnect)
                && quickConnect == Identity("https://DEMO-NAS.quickconnect.to/https_first", ConnectionKind.SynologyQuickConnect),
                "QuickConnect profiles remain tied to the lowercase configured ID and domain independently of the service route");
            check(quickConnect != Identity("https://demo-nas.quickconnect.cn/", ConnectionKind.SynologyQuickConnect)
                && quickConnect != Identity("other-nas", ConnectionKind.SynologyQuickConnect),
                "Different QuickConnect IDs and regional domains receive separate browser profiles");
            check(quickConnect != Identity("https://demo-nas.quickconnect.to/", ConnectionKind.DirectHttps),
                "Direct and QuickConnect modes cannot share a browser profile even when their bootstrap URL matches");

            CultureInfo original = CultureInfo.CurrentCulture;
            bool cultureIndependent;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                cultureIndependent = direct == Identity("HTTPS://NAS.PROFILE.TEST:443/", ConnectionKind.DirectHttps)
                    && quickConnect == Identity("DEMO-NAS", ConnectionKind.SynologyQuickConnect);
            }
            finally { CultureInfo.CurrentCulture = original; }
            check(cultureIndependent, "Browser profile identity is stable across Windows user culture settings");

            bool invalidRejected = true;
            foreach (ConnectionSettings settings in new[] {
                null, new ConnectionSettings { Url = "http://nas.profile.test/", Type = "https" },
                new ConnectionSettings { Url = "https://user:secret@nas.profile.test/", Type = "https" },
                new ConnectionSettings { Url = "https://nas.profile.test/", Type = "unknown" },
                new ConnectionSettings { Url = "https://nas.profile.test/", Type = "quickconnect" }
            })
            {
                bool rejected = false;
                try { BrowserProfileIdentity.ForConnection(settings); }
                catch (ArgumentException) { rejected = true; }
                invalidRejected &= rejected;
            }
            check(invalidRejected, "Invalid or unsupported connections cannot select an unintended browser profile");
        }

        private static string Identity(string url, ConnectionKind kind)
        {
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate(url, kind, out settings)) throw new Exception("Invalid profile fixture.");
            return BrowserProfileIdentity.ForConnection(settings);
        }
    }
}
