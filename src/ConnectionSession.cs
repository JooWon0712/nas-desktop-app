using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NasDesk
{
    // Resolved origins live only for this configured connection, never in shared settings.
    internal sealed class ConnectionSession : IDisposable
    {
        private readonly HashSet<string> verifiedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken token;
        private readonly Func<string, CancellationToken, Task<bool>> verify;
        private bool disposed;
        internal ConnectionAddress Address { get; private set; }
        internal bool IsQuickConnect { get { return Address.QuickConnectId != null; } }

        internal ConnectionSession(string home, Func<string, CancellationToken, Task<bool>> verifier = null)
            : this(ParseAddress(home), verifier) { }

        internal ConnectionSession(string home, ConnectionKind kind, Func<string, CancellationToken, Task<bool>> verifier = null)
            : this(ParseAddress(home, kind), verifier) { }

        private ConnectionSession(ConnectionAddress address, Func<string, CancellationToken, Task<bool>> verifier)
        {
            Address = address;
            token = lifetime.Token;
            if (IsQuickConnect)
            {
                QuickConnectResolver resolver = new QuickConnectResolver(address);
                verify = verifier ?? resolver.VerifyEndpointAsync;
            }
            else verifiedOrigins.Add(Origin(new Uri(Address.Url)));
        }

        private static ConnectionAddress ParseAddress(string home)
        {
            ConnectionAddress address;
            if (!ConnectionAddress.TryParse(home, out address)) throw new ArgumentException("Invalid NAS address.", "home");
            return address;
        }

        private static ConnectionAddress ParseAddress(string home, ConnectionKind kind)
        {
            ConnectionAddress address;
            if (!ConnectionAddress.TryParse(home, kind, out address)) throw new ArgumentException("Invalid NAS address.", "home");
            return address;
        }

        internal bool Allows(string value)
        {
            if (disposed) return false;
            if (value == "about:blank") return true;
            if (value != null && value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) value = value.Substring(5);
            Uri target;
            if (!TrySecureUri(value, out target)) return false;
            if (IsQuickConnect)
            {
                string domain = Address.QuickConnectDomain;
                string host = target.DnsSafeHost;
                if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.Equals("www." + domain, StringComparison.OrdinalIgnoreCase))
                {
                    ConnectionAddress portal;
                    return target.IsDefaultPort && ConnectionAddress.TryParse(target.AbsoluteUri, out portal)
                        && String.Equals(portal.QuickConnectId, Address.QuickConnectId, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(portal.QuickConnectDomain, domain, StringComparison.OrdinalIgnoreCase)
                        && QueryIdentityMatches(target);
                }
                if (host.Equals(Address.QuickConnectId + "." + domain, StringComparison.OrdinalIgnoreCase) && target.IsDefaultPort)
                    return QueryIdentityMatches(target);
            }
            lock (verifiedOrigins) return verifiedOrigins.Contains(Origin(target));
        }

        internal bool CanVerify(string value)
        {
            Uri target;
            if (disposed || !IsQuickConnect || !TrySecureUri(value, out target)) return false;
            // Shared portal pages are never promoted to NAS endpoints.
            foreach (string domain in new[] { "quickconnect.to", "quickconnect.cn" })
                if (target.DnsSafeHost.Equals(domain, StringComparison.OrdinalIgnoreCase)
                    || target.DnsSafeHost.Equals("www." + domain, StringComparison.OrdinalIgnoreCase)) return false;
            if (target.DnsSafeHost.Equals(Address.QuickConnectId + "." + Address.QuickConnectDomain, StringComparison.OrdinalIgnoreCase)
                && target.IsDefaultPort && !QueryIdentityMatches(target)) return false;
            return true;
        }

        internal async Task<bool> VerifyAndAllowAsync(string value)
        {
            if (Allows(value)) return true;
            if (!CanVerify(value)) return false;
            bool verified = await verify(value, token);
            token.ThrowIfCancellationRequested();
            if (!verified || disposed) return false;
            Uri target;
            if (!TrySecureUri(value, out target)) return false;
            lock (verifiedOrigins) verifiedOrigins.Add(Origin(target));
            return true;
        }

        internal bool IsNasOrigin(string value)
        {
            Uri target;
            if (disposed || !TrySecureUri(value, out target)) return false;
            lock (verifiedOrigins) return verifiedOrigins.Contains(Origin(target));
        }

        private bool QueryIdentityMatches(Uri uri)
        {
            foreach (string item in uri.Query.TrimStart('?').Split('&'))
            {
                int separator = item.IndexOf('=');
                string key = Uri.UnescapeDataString(separator < 0 ? item : item.Substring(0, separator)).ToLowerInvariant().Replace("_", "");
                if (key != "id" && key != "serverid" && key != "quickconnectid" && key != "qcid") continue;
                string value = separator < 0 ? "" : Uri.UnescapeDataString(item.Substring(separator + 1)).Replace('+', ' ');
                if (!value.Equals(Address.QuickConnectId, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static bool TrySecureUri(string value, out Uri uri)
        {
            return ConnectionAddress.TrySecureUri(value, out uri);
        }

        private static string Origin(Uri uri) { return uri.GetLeftPart(UriPartial.Authority); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            lock (verifiedOrigins) verifiedOrigins.Clear();
        }
    }
}
