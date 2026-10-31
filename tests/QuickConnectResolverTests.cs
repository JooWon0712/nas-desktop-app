using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal static class QuickConnectResolverTests
    {
        // These checks do not make network requests or read any user settings.
        internal static void Run(Action<bool, string> check)
        {
            ConnectionAddress address;
            check(ConnectionAddress.TryParse("https://quickconnect.to/research-test/file", out address), "Resolver QuickConnect sample parses");
            QuickConnectResolver resolver = new QuickConnectResolver(address);
            check((string)Field(resolver, "servicePath") == "file", "Resolver service path comes from immutable configured URL");
            MethodInfo coordinator = typeof(QuickConnectResolver).GetMethod("IsCoordinator", BindingFlags.Instance | BindingFlags.NonPublic);
            check((bool)coordinator.Invoke(resolver, new object[] { "global.quickconnect.to" }), "Resolver accepts official same-zone coordinator");
            foreach (string unsafeHost in new[] { "global.quickconnect.cn", "global.quickconnect.to.evil.test", "user@global.quickconnect.to", "global.quickconnect.to:443", "a.b.quickconnect.to" })
                check(!(bool)coordinator.Invoke(resolver, new object[] { unsafeHost }), "Resolver rejects coordinator " + unsafeHost);

            string json = "{\"errno\":0,\"server\":{\"serverID\":\"abc\",\"ddns\":\"nas.example.test\",\"fqdn\":\"box.example.test\",\"external\":{\"ip\":\"198.51.100.9\"},\"interface\":[{\"ip\":\"10.0.0.2\",\"ipv6\":[{\"address\":\"2001:db8::7\"},{\"address\":\"fe80::7\"}]},{\"ip\":\"127.0.0.1\"},{\"ip\":\"169.254.169.254\"}]},\"service\":{\"port\":5001,\"ext_port\":5443},\"env\":{\"control_host\":\"us.quickconnect.to\",\"relay_region\":\"us1\"},\"smartdns\":{\"lan\":[\"10-0-0-2.research-test.direct.quickconnect.to\",\"bad@other.test\",\"other.test/path\",\"other.test:443\"],\"host\":\"research-test.direct.quickconnect.cn\"}}";
            Dictionary<string, object> reply = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            MethodInfo parser = typeof(QuickConnectResolver).GetMethod("ParseMetadata", BindingFlags.Instance | BindingFlags.NonPublic);
            object metadata = parser.Invoke(resolver, new object[] { reply });
            check(metadata != null, "Resolver accepts HTTPS device metadata");
            check((string)Field(metadata, "DeviceHash") == "900150983cd24fb0d6963f7d28e17f72", "Resolver hashes opaque device ID rather than public alias");
            HashSet<string> origins = (HashSet<string>)Field(metadata, "Origins");
            check(origins.Contains("https://nas.example.test:5001") && origins.Contains("https://nas.example.test:5443")
                && !origins.Contains("https://nas.example.test:5002") && !origins.Contains("https://nas.example.test.evil.test:5001"),
                "Resolver records exact DDNS origins and only explicit HTTPS ports");
            check(origins.Contains("https://10.0.0.2:5001") && origins.Contains(new Uri("https://[2001:db8::7]:5001/").GetLeftPart(UriPartial.Authority)),
                "Resolver records returned IPv4 and IPv6 NAS candidates");
            check(origins.Contains("https://research-test.us1.quickconnect.to"), "Resolver relay candidate uses same-zone HTTPS 443");
            check(origins.Contains("https://research-test.direct.quickconnect.cn:5001"), "Resolver considers a cross-zone endpoint only when returned by metadata");
            check(!origins.Contains("https://127.0.0.1:5001") && !origins.Contains("https://169.254.169.254:5001")
                && !origins.Contains(new Uri("https://[fe80::7]:5001/").GetLeftPart(UriPartial.Authority)),
                "Resolver excludes loopback and link-local targets");
            check(!origins.Contains("https://other.test:5001") && !origins.Contains("https://other.test:443"),
                "Resolver metadata host injection cannot add an origin");
            check(!resolver.VerifyEndpointAsync("http://nas.example.test:5001/", CancellationToken.None).GetAwaiter().GetResult()
                && !resolver.VerifyEndpointAsync("https://nas.example.test:65536/", CancellationToken.None).GetAwaiter().GetResult(),
                "Resolver rejects HTTP and invalid target ports without a request");
            check(!resolver.VerifyEndpointAsync("https://user:pass@nas.example.test:5001/", CancellationToken.None).GetAwaiter().GetResult()
                && !resolver.VerifyEndpointAsync("https://@nas.example.test:5001/", CancellationToken.None).GetAwaiter().GetResult(),
                "Resolver rejects credential and empty-userinfo targets without a request");
            reply["id"] = "mainapp_http";
            check(parser.Invoke(resolver, new object[] { reply }) == null, "Resolver rejects HTTP service routing metadata");
            reply.Remove("id");
            ((Dictionary<string, object>)reply["server"])["serverID"] = String.Empty;
            check(parser.Invoke(resolver, new object[] { reply }) == null, "Resolver rejects metadata without a device identity");
        }

        private static object Field(object item, string name)
        {
            return item.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(item);
        }
    }
}
