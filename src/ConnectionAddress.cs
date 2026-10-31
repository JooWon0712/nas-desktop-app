using System;
using System.Net;
using System.Net.Sockets;

namespace NasDesk
{
    internal enum ConnectionKind { DirectHttps, SynologyQuickConnect }

    internal sealed class ConnectionAddress
    {
        internal string Url { get; private set; }
        internal string QuickConnectId { get; private set; }
        internal string QuickConnectDomain { get; private set; }

        private ConnectionAddress(Uri uri, string id, string domain)
        {
            Url = uri.AbsoluteUri;
            QuickConnectId = id;
            QuickConnectDomain = domain;
        }

        internal static bool TryParse(string input, ConnectionKind kind, out ConnectionAddress address)
        {
            address = null;
            if (kind == ConnectionKind.SynologyQuickConnect)
            {
                ConnectionAddress legacy;
                if (!TryParse(input, out legacy) || legacy.QuickConnectId == null) return false;
                address = legacy;
                return true;
            }
            if (kind != ConnectionKind.DirectHttps || String.IsNullOrEmpty(input)) return false;

            string value = input;
            if (value.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                IPAddress ip;
                if (IPAddress.TryParse(value, out ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                    value = "[" + value + "]";
                value = "https://" + value;
            }
            Uri uri;
            if (!TrySecureUri(value, out uri)) return false;
            // The selected mode is authoritative, including for QuickConnect-looking hosts.
            address = new ConnectionAddress(uri, null, null);
            return true;
        }

        internal static bool TrySecureUri(string value, out Uri uri)
        {
            uri = null;
            if (String.IsNullOrEmpty(value)) return false;
            foreach (char character in value)
                if (Char.IsWhiteSpace(character) || Char.IsControl(character) || character == '\\') return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !uri.IsWellFormedOriginalString()
                || uri.Scheme != Uri.UriSchemeHttps || String.IsNullOrEmpty(uri.Host)
                || !String.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType == UriHostNameType.Unknown
                || uri.HostNameType == UriHostNameType.Basic) return false;
            int start = value.IndexOf("://", StringComparison.Ordinal);
            if (start < 0) return false;
            start += 3;
            int end = value.IndexOfAny(new[] { '/', '?', '#' }, start);
            if (end < 0) end = value.Length;
            return value.Substring(start, end - start).IndexOf('@') < 0;
        }

        // Used only to preserve the behavior of settings created before a connection type existed.
        internal static bool TryParse(string input, out ConnectionAddress address)
        {
            address = null;
            if (String.IsNullOrEmpty(input)) return false;
            foreach (char character in input)
                if (Char.IsWhiteSpace(character) || Char.IsControl(character) || character == '\\') return false;

            // An unqualified DNS label is a QuickConnect ID, not a local host name.
            if (IsId(input))
            {
                Uri bootstrap = new Uri("https://" + input + ".quickconnect.to/");
                address = new ConnectionAddress(bootstrap, input.ToLowerInvariant(), "quickconnect.to");
                return true;
            }

            if (input.IndexOf("://", StringComparison.Ordinal) < 0 && input.IndexOf('.') < 0 && input.IndexOf(':') < 0)
                return false;

            string value = input.IndexOf("://", StringComparison.Ordinal) >= 0 ? input : "https://" + input;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !uri.IsWellFormedOriginalString()
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || String.IsNullOrEmpty(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo)
                || uri.HostNameType == UriHostNameType.Unknown || uri.HostNameType == UriHostNameType.Basic)
                return false;

            // Uri.UserInfo is empty for an empty userinfo marker such as https://@host/.
            int authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
            int authorityEnd = value.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (authorityEnd < 0) authorityEnd = value.Length;
            if (value.Substring(authorityStart, authorityEnd - authorityStart).IndexOf('@') >= 0) return false;

            string host = uri.DnsSafeHost.ToLowerInvariant();
            string domain = GetQuickConnectDomain(host);
            bool portal = domain != null && (host == domain || host == "www." + domain);
            if (portal)
            {
                if (!uri.IsDefaultPort) return false;
                string path = uri.AbsolutePath;
                int nextSlash = path.IndexOf('/', 1);
                string escapedId = nextSlash < 0 ? path.Substring(1) : path.Substring(1, nextSlash - 1);
                string id;
                try { id = Uri.UnescapeDataString(escapedId); }
                catch (UriFormatException) { return false; }
                if (!IsId(id)) return false;
                string tail = nextSlash < 0 ? "/" : path.Substring(nextSlash);
                Uri canonical = new Uri("https://" + id + "." + domain + tail + uri.Query + uri.Fragment);
                address = new ConnectionAddress(canonical, id.ToLowerInvariant(), domain);
                return true;
            }

            string bootstrapId = null;
            if (domain != null)
            {
                string prefix = host.Substring(0, host.Length - domain.Length - 1);
                if (IsId(prefix)) bootstrapId = prefix;
            }
            if (uri.Scheme == Uri.UriSchemeHttp)
            {
                // Upgrade the official one-label bootstrap URL, never an arbitrary NAS HTTP endpoint.
                if (bootstrapId == null || !uri.IsDefaultPort) return false;
                UriBuilder upgraded = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
                uri = upgraded.Uri;
            }

            // Direct/relay host aliases are not asserted to equal the user's QuickConnect ID.
            // The connection policy must bind those endpoints to the configured connection.
            address = new ConnectionAddress(uri, bootstrapId, domain);
            return true;
        }

        private static string GetQuickConnectDomain(string host)
        {
            foreach (string domain in new[] { "quickconnect.to", "quickconnect.cn" })
                if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)) return domain;
            return null;
        }

        private static bool IsId(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 63 || !IsAlphaNumeric(value[0]) || !IsAlphaNumeric(value[value.Length - 1])) return false;
            foreach (char character in value)
                if (!IsAlphaNumeric(character) && character != '-') return false;
            return true;
        }

        private static bool IsAlphaNumeric(char character)
        {
            return character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z'
                || character >= '0' && character <= '9';
        }
    }
}
