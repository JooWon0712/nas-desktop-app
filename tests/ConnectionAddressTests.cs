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

            check(Direct("nas", "https://nas/") && Direct("nas:8443/ui/#/login", "https://nas:8443/ui/#/login"),
                "Direct HTTPS mode treats an unqualified NAS name as a local host rather than a QuickConnect ID");
            check(Direct("qnap.local:443/cgi-bin/", "https://qnap.local/cgi-bin/")
                && Direct("https://asustor.example:8001/portal/", "https://asustor.example:8001/portal/")
                && Direct("https://truenas.example/ui/", "https://truenas.example/ui/"),
                "Generic NAS HTTPS mode preserves manufacturer-independent ports and application routes");
            check(Direct("192.0.2.10:9443/", "https://192.0.2.10:9443/")
                && Direct("[2001:db8::10]:8443/ui/", "https://[2001:db8::10]:8443/ui/")
                && Direct("2001:db8::10", "https://[2001:db8::10]/"),
                "Direct mode supports explicit IPv4 and IPv6 NAS addresses");
            check(Direct("https://demo-nas.quickconnect.to/", "https://demo-nas.quickconnect.to/")
                && Direct("https://quickconnect.to/demo-nas/", "https://quickconnect.to/demo-nas/"),
                "An explicitly direct QuickConnect-looking address never activates the QuickConnect resolver");
            ConnectionAddress explicitQuickConnect;
            check(ConnectionAddress.TryParse("Demo-Nas", ConnectionKind.SynologyQuickConnect, out explicitQuickConnect)
                && explicitQuickConnect.Url == "https://demo-nas.quickconnect.to/"
                && explicitQuickConnect.QuickConnectId == "demo-nas"
                && !ExplicitAccepts("https://qnap.example/", ConnectionKind.SynologyQuickConnect)
                && !ExplicitAccepts("https://device-alias.us2.quickconnect.to/", ConnectionKind.SynologyQuickConnect),
                "Explicit Synology mode requires an actual QuickConnect ID or its bootstrap URL");
            check(!ExplicitAccepts("http://nas.local/", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("http://demo-nas.quickconnect.to/", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("https://@nas.local/", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("https://user:secret@nas.local/", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("nas\\share", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("nas\r\n", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("javascript:alert(1)", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("file:///tmp/test", ConnectionKind.DirectHttps)
                && !ExplicitAccepts("nas", (ConnectionKind)999),
                "Explicit direct mode rejects HTTP, credentials, unsafe characters, unsupported schemes and invalid modes");
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

        private static bool Direct(string value, string expectedUrl)
        {
            ConnectionAddress address;
            return ConnectionAddress.TryParse(value, ConnectionKind.DirectHttps, out address)
                && address.Url == expectedUrl && address.QuickConnectId == null && address.QuickConnectDomain == null;
        }

        private static bool ExplicitAccepts(string value, ConnectionKind kind)
        {
            ConnectionAddress address;
            return ConnectionAddress.TryParse(value, kind, out address);
        }
    }
}
