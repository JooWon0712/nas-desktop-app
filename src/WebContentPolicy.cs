using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal static class WebContentPolicy
    {
        internal static bool AllowsRequest(ConnectionSession session, string value, bool allowExternal, bool quickConnectBootstrap)
        {
            if (value == "about:blank") return true;
            Uri uri;
            if (String.IsNullOrEmpty(value) || !Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme == "data" || uri.Scheme == "blob") return true;
            if (uri.Scheme != "https" && uri.Scheme != "wss") return false;
            string httpsValue = uri.Scheme == "wss" ? new UriBuilder(uri) { Scheme = "https", Port = uri.Port }.Uri.AbsoluteUri : value;
            if (session.IsNasOrigin(httpsValue)) return true;
            // Only the configured QuickConnect bootstrap document gets this
            // compatibility exception. It is never a NAS origin or permission grant.
            return allowExternal || (uri.Scheme == "https" && quickConnectBootstrap && session.IsQuickConnect);
        }

        internal static bool ReadExternalContentAllowed(string path, string profileName)
        {
            if (!File.Exists(path)) return false;
            try
            {
                Dictionary<string, bool> values = ReadValues(path);
                bool allowed;
                return values != null && values.TryGetValue(profileName, out allowed) && allowed;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidDataException) { return false; }
        }

        internal static void SaveExternalContentAllowed(string path, string profileName, bool allowed)
        {
            Dictionary<string, bool> values = new Dictionary<string, bool>();
            if (File.Exists(path))
            {
                try { values = ReadValues(path); }
                catch (ArgumentException) { throw new InvalidDataException("보안 설정 파일의 형식을 확인해 주세요."); }
                catch (InvalidOperationException) { throw new InvalidDataException("보안 설정 파일의 형식을 확인해 주세요."); }
            }
            values[profileName] = allowed;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(values), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static Dictionary<string, bool> ReadValues(string path)
        {
            Dictionary<string, object> raw = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
            if (raw == null) throw new InvalidDataException("보안 설정 파일의 형식을 확인해 주세요.");
            Dictionary<string, bool> values = new Dictionary<string, bool>();
            foreach (KeyValuePair<string, object> entry in raw)
            {
                if (!(entry.Value is bool)) throw new InvalidDataException("보안 설정 파일의 형식을 확인해 주세요.");
                values.Add(entry.Key, (bool)entry.Value);
            }
            return values;
        }
    }
}
