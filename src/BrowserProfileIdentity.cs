using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NasDesk
{
    internal static class BrowserProfileIdentity
    {
        internal static string ForConnection(ConnectionSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            ConnectionKind kind;
            try { kind = settings.Kind; }
            catch (InvalidDataException error) { throw new ArgumentException("프로필을 구분할 NAS 주소와 접속 방식을 확인해 주세요.", "settings", error); }
            ConnectionAddress address;
            if (!ConnectionAddress.TryParse(settings.Url, kind, out address))
                throw new ArgumentException("프로필을 구분할 NAS 주소와 접속 방식을 확인해 주세요.", "settings");

            string identity;
            if (kind == ConnectionKind.SynologyQuickConnect)
                identity = "quickconnect\n" + address.QuickConnectId.ToLowerInvariant() + "\n" + address.QuickConnectDomain.ToLowerInvariant();
            else
            {
                Uri uri = new Uri(address.Url);
                identity = "direct\n" + uri.Scheme.ToLowerInvariant() + "\n" + uri.IdnHost.ToLowerInvariant()
                    + "\n" + uri.Port.ToString(CultureInfo.InvariantCulture);
            }

            byte[] digest;
            using (SHA256 hash = SHA256.Create()) digest = hash.ComputeHash(Encoding.UTF8.GetBytes("NasDesk.Profile.v1\n" + identity));
            StringBuilder name = new StringBuilder(64);
            foreach (byte value in digest) name.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return name.ToString();
        }
    }
}
