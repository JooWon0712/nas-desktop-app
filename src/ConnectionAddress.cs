using System;

namespace NasDesk
{
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
