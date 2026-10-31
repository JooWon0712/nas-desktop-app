using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal sealed class ConnectionSettings
    {
        internal const string ProtectionPurpose = "connection-settings";
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
            string stored;
            try { stored = File.ReadAllText(path); }
            catch (FileNotFoundException) { return new ConnectionSettings(); }
            catch (DirectoryNotFoundException) { return new ConnectionSettings(); }
            return ReadDocument(LocalProtectedJson.Decode(stored, ProtectionPurpose));
        }

        internal static bool ProtectIfNeeded(string path)
        {
            string stored;
            try { stored = File.ReadAllText(path); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            bool wasProtected;
            ConnectionSettings normalized = ReadDocument(LocalProtectedJson.Decode(stored, ProtectionPurpose, out wasProtected));
            if (wasProtected) return false;
            normalized.Save(path);
            return true;
        }

        private static ConnectionSettings ReadDocument(string json)
        {
            ConnectionSettings settings;
            try { settings = new JavaScriptSerializer().Deserialize<ConnectionSettings>(json); }
            catch (ArgumentException error) { throw new InvalidDataException("NAS 접속 설정 형식을 확인해 주세요.", error); }
            catch (InvalidOperationException error) { throw new InvalidDataException("NAS 접속 설정 형식을 확인해 주세요.", error); }
            if (settings == null) throw new InvalidDataException("NAS 접속 설정을 확인해 주세요.");
            return settings.Normalize();
        }

        public void Save(string path)
        {
            ConnectionSettings normalized = Normalize();
            LocalProtectedJson.WriteAtomic(path, new JavaScriptSerializer().Serialize(normalized), ProtectionPurpose);
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

    // DPAPI protects data at rest for the current Windows user. It does not
    // isolate this app from other processes already running as that user.
    internal static class LocalProtectedJson
    {
        internal static string Decode(string stored, string purpose)
        {
            bool wasProtected;
            return Decode(stored, purpose, out wasProtected);
        }

        internal static string Decode(string stored, string purpose, out bool wasProtected)
        {
            wasProtected = false;
            object parsed;
            try { parsed = new JavaScriptSerializer().DeserializeObject(stored); }
            catch (ArgumentException error) { throw InvalidProtection(error); }
            catch (InvalidOperationException error) { throw InvalidProtection(error); }
            IDictionary<string, object> document = parsed as IDictionary<string, object>;
            if (document == null || (!document.ContainsKey("Protection") && !document.ContainsKey("Payload"))) return stored;
            object protection, version, payload;
            if (document.Count != 3 || !document.TryGetValue("Protection", out protection)
                || !String.Equals(protection as string, "DPAPI.CurrentUser", StringComparison.Ordinal)
                || !document.TryGetValue("Version", out version) || !(version is int) || (int)version != 1
                || !document.TryGetValue("Payload", out payload) || String.IsNullOrEmpty(payload as string))
                throw InvalidProtection(null);
            byte[] plaintext = null;
            try
            {
                plaintext = ProtectedData.Unprotect(Convert.FromBase64String((string)payload), Entropy(purpose), DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plaintext);
                wasProtected = true;
                return json;
            }
            catch (FormatException error) { throw InvalidProtection(error); }
            catch (CryptographicException error) { throw InvalidProtection(error); }
            finally { if (plaintext != null) Array.Clear(plaintext, 0, plaintext.Length); }
        }

        internal static void WriteAtomic(string path, string json, string purpose)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            string encoded;
            try
            {
                byte[] protectedData = ProtectedData.Protect(plaintext, Entropy(purpose), DataProtectionScope.CurrentUser);
                encoded = new JavaScriptSerializer().Serialize(new ProtectedDocument {
                    Protection = "DPAPI.CurrentUser", Version = 1, Payload = Convert.ToBase64String(protectedData)
                });
            }
            finally { Array.Clear(plaintext, 0, plaintext.Length); }
            string absolutePath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(absolutePath);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Path.GetFileName(absolutePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, encoded, new UTF8Encoding(false));
                if (File.Exists(absolutePath)) File.Replace(temporary, absolutePath, null);
                else File.Move(temporary, absolutePath);
            }
            finally { File.Delete(temporary); }
        }

        private static byte[] Entropy(string purpose) { return Encoding.UTF8.GetBytes("NasDesk.LocalData.v1:" + purpose); }
        private static InvalidDataException InvalidProtection(Exception inner)
        {
            return new InvalidDataException("이 Windows 사용자 계정에서 설정 파일을 읽을 수 없습니다. 파일 형식이나 보호 데이터를 확인해 주세요.", inner);
        }
        private sealed class ProtectedDocument
        {
            public string Protection { get; set; }
            public int Version { get; set; }
            public string Payload { get; set; }
        }
    }
}
