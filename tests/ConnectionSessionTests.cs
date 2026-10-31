using System;
using System.Threading;
using System.Threading.Tasks;

namespace NasDesk
{
    internal static class ConnectionSessionTests
    {
        internal static async Task RunAsync(Action<bool, string> check)
        {
            int explicitDirectVerifications = 0;
            using (ConnectionSession local = new ConnectionSession("nas:8443/ui/", ConnectionKind.DirectHttps,
                delegate(string target, CancellationToken cancellation)
                {
                    explicitDirectVerifications++;
                    return Task.FromResult(true);
                }))
            {
                check(!local.IsQuickConnect && local.Address.QuickConnectId == null
                    && local.Allows("https://nas:8443/ui/login") && local.IsNasOrigin("https://nas:8443/")
                    && !local.Allows("https://nas:9443/") && !local.Allows("https://nas.attacker.invalid:8443/")
                    && !local.Allows("http://nas:8443/") && !local.Allows("https://@nas:8443/"),
                    "An explicit local NAS connection preserves its exact HTTPS host and port boundary");
                bool expanded = await local.VerifyAndAllowAsync("https://nas.quickconnect.to/");
                check(!expanded && explicitDirectVerifications == 0,
                    "Direct mode never sends an unqualified LAN name to the QuickConnect verifier");
            }
            using (ConnectionSession directPortal = new ConnectionSession("https://demo-nas.quickconnect.to/", ConnectionKind.DirectHttps))
                check(!directPortal.IsQuickConnect && directPortal.IsNasOrigin("https://demo-nas.quickconnect.to/")
                    && !directPortal.Allows("https://quickconnect.to/demo-nas/")
                    && !directPortal.CanVerify("https://192.0.2.10:5001/"),
                    "Explicit direct sessions are not reclassified from the canonical URL host");
            using (ConnectionSession ipv6 = new ConnectionSession("[2001:db8::10]:8443/ui/", ConnectionKind.DirectHttps))
                check(ipv6.Allows("https://[2001:db8::10]:8443/ui/login")
                    && !ipv6.Allows("https://[2001:db8::11]:8443/") && !ipv6.Allows("https://[2001:db8::10]:9443/"),
                    "IPv6 direct sessions keep the configured address and port boundary");
            using (ConnectionSession explicitSynology = new ConnectionSession("demo-nas", ConnectionKind.SynologyQuickConnect,
                (target, cancellation) => Task.FromResult(false)))
                check(explicitSynology.IsQuickConnect && explicitSynology.Allows("https://demo-nas.quickconnect.to/")
                    && !explicitSynology.IsNasOrigin("https://demo-nas.quickconnect.to/"),
                    "Explicit Synology sessions preserve QuickConnect endpoint verification");

            int rejectedVerifications = 0;
            using (ConnectionSession session = new ConnectionSession("demo-nas", delegate(string target, CancellationToken cancellation)
            {
                rejectedVerifications++;
                return Task.FromResult(false);
            }))
            {
                check(session.Allows("https://demo-nas.quickconnect.to/#/signin")
                    && session.Allows("https://quickconnect.to/demo-nas/")
                    && session.Allows("https://www.quickconnect.to/demo-nas/file?id=demo-nas"),
                    "QuickConnect bootstrap and shared portal paths stay bound to the configured ID");

                check(!session.Allows("https://quickconnect.to/other-nas/")
                    && !session.Allows("https://www.quickconnect.to/other-nas/")
                    && !session.CanVerify("https://quickconnect.to/other-nas/")
                    && !session.CanVerify("https://www.quickconnect.to/demo-nas/"),
                    "Shared portal pages cannot switch IDs or be promoted to verified NAS origins");

                check(!session.Allows("https://demo-nas.quickconnect.to/?id=other-nas")
                    && !session.CanVerify("https://demo-nas.quickconnect.to/?id=other-nas")
                    && !session.Allows("https://quickconnect.to/demo-nas/?server_id=other-nas")
                    && !session.Allows("https://demo-nas.quickconnect.to/?id=demo-nas&id=other-nas"),
                    "Identity-changing and duplicate conflicting query parameters are rejected");

                check(!session.Allows("https://demo-nas.quickconnect.to/?%69%64=other-nas")
                    && !session.Allows("https://quickconnect.to/demo-nas/?server%5Fid=other-nas")
                    && !session.Allows("https://demo-nas.quickconnect.to/?quick%5Fconnect%5Fid=other-nas"),
                    "Encoded query keys cannot bypass QuickConnect identity checks");

                check(!session.Allows("https://other-nas.quickconnect.to/")
                    && !session.Allows("https://demo-nas.direct.quickconnect.to:5001/")
                    && !session.Allows("https://demo-nas.us2.quickconnect.to/")
                    && !session.Allows("https://demo-nas.quickconnect.to.attacker.invalid/"),
                    "Unverified IDs, direct aliases, relay aliases and suffix lookalikes gain no implicit trust");

                using (ConnectionSession china = new ConnectionSession("https://quickconnect.cn/demo-nas", (target, cancellation) => Task.FromResult(false)))
                    check(!session.Allows("https://demo-nas.quickconnect.cn/")
                        && china.Allows("https://demo-nas.quickconnect.cn/")
                        && !china.Allows("https://demo-nas.quickconnect.to/"),
                        "QuickConnect to and cn zones are not interchangeable even for the same ID");

                check(!session.Allows("http://demo-nas.quickconnect.to/")
                    && !session.CanVerify("http://nas-endpoint.invalid:5000/")
                    && !session.Allows("https://user:secret@demo-nas.quickconnect.to/")
                    && !session.CanVerify("https://@nas-endpoint.invalid/")
                    && !session.Allows("blob:https://unverified-endpoint.invalid/fixture")
                    && !session.CanVerify("blob:https://unverified-endpoint.invalid/fixture"),
                    "HTTP, userinfo and blobs from unverified origins remain blocked");

                check(session.Allows("about:blank")
                    && !session.IsNasOrigin("about:blank")
                    && !session.IsNasOrigin("https://demo-nas.quickconnect.to/")
                    && !session.IsNasOrigin("https://quickconnect.to/demo-nas/"),
                    "Blank popup and portal navigation do not grant native NAS-origin permissions");

                bool accepted = await session.VerifyAndAllowAsync("https://unrelated-alias.us2.quickconnect.to/");
                check(!accepted && rejectedVerifications == 1
                    && !session.Allows("https://unrelated-alias.us2.quickconnect.to/"),
                    "A failed endpoint identity verification never adds the candidate origin");
            }

            int directVerifierCalls = 0;
            using (ConnectionSession direct = new ConnectionSession("https://nas.example:5001/", delegate(string target, CancellationToken cancellation)
            {
                directVerifierCalls++;
                return Task.FromResult(true);
            }))
            {
                bool expanded = await direct.VerifyAndAllowAsync("https://other.example:5001/");
                check(direct.Allows("https://nas.example:5001/webman/")
                    && direct.Allows("blob:https://nas.example:5001/fixture")
                    && direct.IsNasOrigin("https://nas.example:5001/")
                    && !direct.Allows("https://nas.example:5002/")
                    && !expanded && directVerifierCalls == 0,
                    "Explicit direct HTTPS connections retain their exact-origin boundary");
            }

            const string verifiedEndpoint = "https://192.0.2.44:5001/#/signin";
            int successfulVerifications = 0;
            ConnectionSession verified = new ConnectionSession("demo-nas", delegate(string target, CancellationToken cancellation)
            {
                successfulVerifications++;
                return Task.FromResult(target == verifiedEndpoint);
            });
            bool previouslyAllowed = verified.Allows(verifiedEndpoint);
            bool verifiedResult = await verified.VerifyAndAllowAsync(verifiedEndpoint);
            check(!previouslyAllowed && verifiedResult && successfulVerifications == 1
                && verified.Allows("https://192.0.2.44:5001/webman/")
                && verified.Allows("blob:https://192.0.2.44:5001/fixture"),
                "Verified endpoint adds only its exact HTTPS origin and that origin's blob navigation");

            check(!verified.Allows("https://192.0.2.44:5002/")
                && !verified.Allows("http://192.0.2.44:5001/")
                && !verified.Allows("https://192.0.2.45:5001/")
                && !verified.Allows("https://192.0.2.44.attacker.invalid:5001/"),
                "Endpoint verification does not spread to other ports, schemes or hosts");

            check(verified.IsNasOrigin("https://192.0.2.44:5001/webman/")
                && !verified.IsNasOrigin("https://demo-nas.quickconnect.to/")
                && !verified.IsNasOrigin("https://quickconnect.to/demo-nas/"),
                "Native NAS-origin permissions require the verified endpoint rather than the portal");
            verified.Dispose();
            check(!verified.Allows(verifiedEndpoint)
                && !verified.Allows("blob:https://192.0.2.44:5001/fixture")
                && !verified.IsNasOrigin(verifiedEndpoint)
                && !verified.CanVerify(verifiedEndpoint),
                "Disposing a configured connection revokes all of its resolved permissions");

            TaskCompletionSource<bool> delayed = new TaskCompletionSource<bool>();
            ConnectionSession old = new ConnectionSession("old-nas", (target, cancellation) => delayed.Task);
            Task<bool> pending = old.VerifyAndAllowAsync(verifiedEndpoint);
            old.Dispose();
            delayed.SetResult(true);
            bool staleRejected;
            try { staleRejected = !await pending; }
            catch (OperationCanceledException) { staleRejected = true; }
            using (ConnectionSession replacement = new ConnectionSession("new-nas", (target, cancellation) => Task.FromResult(false)))
                check(staleRejected && !old.Allows(verifiedEndpoint)
                    && !replacement.Allows(verifiedEndpoint)
                    && !replacement.IsNasOrigin(verifiedEndpoint),
                    "Late verification from a disposed connection cannot authorize the old or replacement session");
        }
    }
}
