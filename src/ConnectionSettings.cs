using System;
using System.IO;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal sealed class ConnectionSettings
    {
        public string Url { get; set; }
        // Null means a legacy file. New UI selections always save an explicit type.
        public string Type { get; set; }

        internal ConnectionKind Kind
        {
            get
            {
                if (Type == null)
                {
                    if (String.IsNullOrEmpty(Url)) return ConnectionKind.DirectHttps;
                    ConnectionAddress legacy;
                    if (ConnectionAddress.TryParse(Url, out legacy))
                        return legacy.QuickConnectId == null ? ConnectionKind.DirectHttps : ConnectionKind.SynologyQuickConnect;
                }
                ConnectionKind kind;
                if (TryKind(Type, out kind)) return kind;
                throw new InvalidDataException("NAS 접속 방식이나 주소를 확인해 주세요.");
            }
        }

        internal static bool TryCreate(string input, ConnectionKind kind, out ConnectionSettings settings)
        {
            settings = null;
            ConnectionAddress address;
            if (!ConnectionAddress.TryParse(input, kind, out address)) return false;
            settings = new ConnectionSettings { Url = address.Url, Type = TypeName(kind) };
            return true;
        }

        public static bool IsValid(string value)
        {
            Uri uri;
            return ConnectionAddress.TrySecureUri(value, out uri);
        }

        public static bool IsInternal(string target, string home)
        {
            if (target == "about:blank") return true;
            if (target != null && target.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) target = target.Substring(5);
            Uri destination, origin;
            return ConnectionAddress.TrySecureUri(target, out destination)
                && ConnectionAddress.TrySecureUri(home, out origin)
                && destination.GetLeftPart(UriPartial.Authority).Equals(origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
        }

        public static ConnectionSettings Read(string path)
        {
            if (!File.Exists(path)) return new ConnectionSettings();
            ConnectionSettings settings;
            try { settings = new JavaScriptSerializer().Deserialize<ConnectionSettings>(File.ReadAllText(path)); }
            catch (ArgumentException error) { throw new InvalidDataException("NAS 접속 설정 형식을 확인해 주세요.", error); }
            catch (InvalidOperationException error) { throw new InvalidDataException("NAS 접속 설정 형식을 확인해 주세요.", error); }
            if (settings == null) throw new InvalidDataException("NAS 접속 설정을 확인해 주세요.");
            return settings.Normalize();
        }

        public void Save(string path)
        {
            ConnectionSettings normalized = Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(normalized));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            Url = normalized.Url;
            Type = normalized.Type;
        }

        private ConnectionSettings Normalize()
        {
            ConnectionAddress address;
            if (Type == null)
            {
                if (!ConnectionAddress.TryParse(Url, out address)) throw InvalidAddress();
                return new ConnectionSettings { Url = address.Url,
                    Type = TypeName(address.QuickConnectId == null ? ConnectionKind.DirectHttps : ConnectionKind.SynologyQuickConnect) };
            }
            ConnectionKind kind;
            ConnectionSettings normalized;
            if (!TryKind(Type, out kind) || !TryCreate(Url, kind, out normalized)) throw InvalidAddress();
            return normalized;
        }

        private static bool TryKind(string type, out ConnectionKind kind)
        {
            if (String.Equals(type, "https", StringComparison.OrdinalIgnoreCase)) { kind = ConnectionKind.DirectHttps; return true; }
            if (String.Equals(type, "quickconnect", StringComparison.OrdinalIgnoreCase)) { kind = ConnectionKind.SynologyQuickConnect; return true; }
            kind = ConnectionKind.DirectHttps;
            return false;
        }

        private static string TypeName(ConnectionKind kind)
        {
            return kind == ConnectionKind.SynologyQuickConnect ? "quickconnect" : "https";
        }

        private static InvalidDataException InvalidAddress()
        {
            return new InvalidDataException("NAS HTTPS 주소 또는 Synology QuickConnect ID·주소를 확인하세요. 주소에 계정 정보를 포함하지 마세요.");
        }
    }
}
