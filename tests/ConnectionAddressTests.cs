using System;

namespace NasDesk
{
    internal static class ConnectionAddressTests
    {
        internal static void Run(Action<bool, string> check)
        {
            check(Parses("Demo-Nas", "https://demo-nas.quickconnect.to/", "demo-nas", "quickconnect.to"), "Bare QuickConnect ID becomes an HTTPS bootstrap URL");
            check(Parses("quickconnect.to/Demo-Nas", "https://demo-nas.quickconnect.to/", "demo-nas", "quickconnect.to"), "Scheme-less QuickConnect portal URL is normalized");
            check(Parses("http://www.quickconnect.to/demo-nas/file/?test=1#/signin", "https://demo-nas.quickconnect.to/file/?test=1#/signin", "demo-nas", "quickconnect.to"), "Portal HTTP upgrades to HTTPS and preserves app route, query and fragment");
            check(Parses("https://quickconnect.cn/demo-nas", "https://demo-nas.quickconnect.cn/", "demo-nas", "quickconnect.cn"), "Explicit QuickConnect China zone is retained");
            check(Parses("demo-nas.quickconnect.to", "https://demo-nas.quickconnect.to/", "demo-nas", "quickconnect.to"), "QuickConnect bootstrap host is accepted without a scheme");
            check(Parses("http://demo-nas.quickconnect.cn/#/signin", "https://demo-nas.quickconnect.cn/#/signin", "demo-nas", "quickconnect.cn"), "Official ID bootstrap host upgrades HTTP without changing zone");
            check(Parses("https://syn4-192-0-2-10.device-alias.direct.quickconnect.to:5001/#/signin", "https://syn4-192-0-2-10.device-alias.direct.quickconnect.to:5001/#/signin", null, "quickconnect.to"), "Direct QuickConnect endpoint is preserved without inventing an ID-alias binding");
            check(Parses("https://device-alias.us2.quickconnect.to/#/signin", "https://device-alias.us2.quickconnect.to/#/signin", null, "quickconnect.to"), "QuickConnect relay endpoint is preserved without inventing an ID-alias binding");
            check(Parses("https://nas.example:5001/#/signin", "https://nas.example:5001/#/signin", null, null), "Direct HTTPS NAS URL preserves the DSM hash route");
            check(Parses("nas.example:5001/", "https://nas.example:5001/", null, null), "Scheme-less direct NAS host and port use HTTPS");
            check(Parses("https://demo.quickconnect.to.attacker.invalid/", "https://demo.quickconnect.to.attacker.invalid/", null, null), "Lookalike suffix is never classified as QuickConnect");
            check(!Accepts("https://quickconnect.to/") && !Accepts("https://www.quickconnect.cn/"), "Portal URLs require a QuickConnect ID");
            check(!Accepts("https://quickconnect.to/demo%2Fother") && !Accepts("https://quickconnect.to/demo%5Cother"), "Encoded separators are rejected inside the QuickConnect ID");
            check(!Accepts("http://nas.example:5000/") && !Accepts("http://device-alias.direct.quickconnect.to:5000/"), "Arbitrary direct HTTP endpoints are rejected");
            check(!Accepts("https://user:secret@nas.example/") && !Accepts("https://@nas.example/") && !Accepts("https://a@b@nas.example/"), "All userinfo markers are rejected");
            check(!Accepts("demo nas") && !Accepts("demo\r\n") && !Accepts("demo_") && !Accepts("-demo")
                && !Accepts("javascript:alert(1)") && !Accepts("file:///tmp/test") && !Accepts("data:text/plain,test"), "Malformed IDs, whitespace, controls and unsupported schemes are rejected");
        }

        private static bool Parses(string value, string expectedUrl, string expectedId, string expectedDomain)
        {
            ConnectionAddress address;
            return ConnectionAddress.TryParse(value, out address) && address.Url == expectedUrl
                && address.QuickConnectId == expectedId && address.QuickConnectDomain == expectedDomain;
        }

        private static bool Accepts(string value)
        {
            ConnectionAddress address;
            return ConnectionAddress.TryParse(value, out address);
        }
    }
}
