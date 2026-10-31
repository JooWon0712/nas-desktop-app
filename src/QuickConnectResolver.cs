using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NasDesk
{
    // Confirms an otherwise-unrecognizable HTTPS redirect against Synology's
    // metadata for this specific configured ID, then its device ping response.
    internal sealed class QuickConnectResolver
    {
        private const int MaximumResponseBytes = 262144;
        private readonly string quickConnectId;
        private readonly string domain;
        private readonly string servicePath;
        private readonly SemaphoreSlim metadataGate = new SemaphoreSlim(1, 1);
        private Metadata cachedMetadata;

        private sealed class Metadata
        {
            internal string DeviceHash;
            internal readonly long CreatedAt = Stopwatch.GetTimestamp();
            internal readonly HashSet<string> Origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        internal QuickConnectResolver(ConnectionAddress address)
        {
            if (address == null) throw new ArgumentNullException("address");
            quickConnectId = address.QuickConnectId;
            domain = address.QuickConnectDomain;
            servicePath = String.Empty;
            foreach (string segment in new Uri(address.Url).AbsolutePath.Split('/'))
                if (segment.Length != 0 && segment != "https_first") { servicePath = segment; break; }
        }

        internal async Task<bool> VerifyEndpointAsync(string target, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Uri targetUri;
            if (String.IsNullOrEmpty(quickConnectId) || (domain != "quickconnect.to" && domain != "quickconnect.cn")
                || !TrySecureUri(target, out targetUri)) return false;

            using (CancellationTokenSource overall = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                overall.CancelAfter(14000);
                try
                {
                    Metadata previousCache = cachedMetadata;
                    Metadata metadata = await GetMetadataAsync(false, overall.Token);
                    overall.Token.ThrowIfCancellationRequested();
                    string origin = Origin(targetUri);
                    if (await VerifyIdentityAsync(metadata, origin, overall.Token)) return true;
                    // A changed NAS address must not stay rejected behind a cached route.
                    if (metadata == null || !Object.ReferenceEquals(metadata, previousCache)) return false;
                    metadata = await GetMetadataAsync(true, overall.Token);
                    return await VerifyIdentityAsync(metadata, origin, overall.Token);
                }
                catch (OperationCanceledException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("QuickConnect 연결 주소 확인 시간이 초과되었습니다.");
                }
            }
        }

        private static async Task<bool> VerifyIdentityAsync(Metadata metadata, string origin, CancellationToken cancellation)
        {
            if (metadata == null || !metadata.Origins.Contains(origin)) return false;
            Uri ping = new Uri(origin + "/webman/pingpong.cgi?action=cors&quickconnect=true");
            object reply = await ReadJsonAsync(ping, null, cancellation);
            cancellation.ThrowIfCancellationRequested();
            string identity = Text(Map(reply), "ezid");
            return identity != null && identity.Equals(metadata.DeviceHash, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Metadata> GetMetadataAsync(bool refresh, CancellationToken cancellation)
        {
            await metadataGate.WaitAsync(cancellation);
            try
            {
                if (!refresh && cachedMetadata != null
                    && (Stopwatch.GetTimestamp() - cachedMetadata.CreatedAt) / (double)Stopwatch.Frequency < 30) return cachedMetadata;
                Metadata loaded = await FetchMetadataAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                cachedMetadata = loaded;
                return loaded;
            }
            finally { metadataGate.Release(); }
        }

        private async Task<Metadata> FetchMetadataAsync(CancellationToken cancellation)
        {
            Queue<string> pending = new Queue<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            pending.Enqueue("global." + domain);
            string payload = new JavaScriptSerializer().Serialize(new[] { new
            {
                version = 1, command = "get_server_info", stop_when_error = false,
                stop_when_success = false, id = "mainapp_https", serverID = quickConnectId,
                is_gofile = false, path = servicePath
            } });
            int attempts = 0;
            while (pending.Count != 0 && attempts++ < 3)
            {
                cancellation.ThrowIfCancellationRequested();
                string coordinator = pending.Dequeue();
                if (!IsCoordinator(coordinator) || !visited.Add(coordinator)) continue;
                object response = await ReadJsonAsync(new Uri("https://" + coordinator + "/Serv.php"), payload, cancellation);
                object[] replies = response as object[];
                if (replies == null || replies.Length != 1) return null;
                Dictionary<string, object> reply = Map(replies[0]);
                if (reply == null) return null;
                Metadata parsed = ParseMetadata(reply);
                if (parsed != null) return parsed;
                object[] sites = Value(reply, "sites") as object[];
                if (sites != null)
                    foreach (object site in sites)
                    {
                        string host = site as string;
                        if (host != null && IsCoordinator(host) && !visited.Contains(host) && pending.Count < 8) pending.Enqueue(host);
                    }
            }
            return null;
        }

        private Metadata ParseMetadata(Dictionary<string, object> reply)
        {
            if (Integer(reply, "errno") != 0) return null;
            Dictionary<string, object> server = Map(Value(reply, "server"));
            Dictionary<string, object> service = Map(Value(reply, "service"));
            Dictionary<string, object> environment = Map(Value(reply, "env"));
            string device = Text(server, "serverID");
            int port = Integer(service, "port");
            if (server == null || service == null || environment == null || String.IsNullOrEmpty(device)
                || device.Length > 512 || port < 1 || port > 65535) return null;
            string serviceId = Text(reply, "id");
            if (serviceId != null && serviceId != "mainapp_https") return null;
            Metadata metadata = new Metadata();
            using (MD5 hash = MD5.Create())
                metadata.DeviceHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(device))).Replace("-", String.Empty).ToLowerInvariant();
            int externalPort = Integer(service, "ext_port");
            Dictionary<string, object> smart = Map(Value(reply, "smartdns"));
            AddArray(metadata, Value(smart, "lan") as object[], port, externalPort);
            AddArray(metadata, Value(smart, "lanv6") as object[], port, externalPort);
            AddHost(metadata, Text(smart, "host"), port, externalPort);
            AddHost(metadata, Text(server, "ddns"), port, externalPort);
            AddHost(metadata, Text(server, "fqdn"), port, externalPort);
            AddHost(metadata, Text(Map(Value(server, "external")), "ip"), port, externalPort);
            object[] interfaces = Value(server, "interface") as object[];
            if (interfaces != null)
                foreach (object entry in interfaces)
                {
                    Dictionary<string, object> iface = Map(entry);
                    AddHost(metadata, Text(iface, "ip"), port, externalPort);
                    object[] addresses = Value(iface, "ipv6") as object[];
                    if (addresses != null)
                        foreach (object address in addresses) AddHost(metadata, Text(Map(address), "address"), port, externalPort);
                }
            string region = Text(environment, "relay_region");
            string control = Text(environment, "control_host");
            if (IsLabel(region) && IsCoordinator(control)) AddHost(metadata, quickConnectId + "." + region + "." + domain, 443, 0);
            return metadata.Origins.Count == 0 ? null : metadata;
        }

        private static void AddArray(Metadata metadata, object[] hosts, int port, int externalPort)
        {
            if (hosts == null) return;
            foreach (object host in hosts) AddHost(metadata, host as string, port, externalPort);
        }

        private static void AddHost(Metadata metadata, string host, int port, int externalPort)
        {
            if (String.IsNullOrEmpty(host) || host == "NULL" || host.Length > 253) return;
            foreach (char character in host)
                if (Char.IsWhiteSpace(character) || Char.IsControl(character) || "/\\@?#%".IndexOf(character) >= 0) return;
            IPAddress ip;
            bool isIp = IPAddress.TryParse(host.Trim('[', ']'), out ip);
            if (host.IndexOf(':') >= 0 && (!isIp || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)) return;
            if (isIp && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)
                || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || IsRestrictedIpv4(ip))) return;
            foreach (int candidatePort in new[] { port, externalPort })
            {
                if (candidatePort < 1 || candidatePort > 65535) continue;
                try
                {
                    Uri uri = new UriBuilder(Uri.UriSchemeHttps, isIp ? ip.ToString() : host, candidatePort).Uri;
                    if (uri.HostNameType == UriHostNameType.Unknown || uri.HostNameType == UriHostNameType.Basic) continue;
                    metadata.Origins.Add(Origin(uri));
                }
                catch (UriFormatException) { }
            }
        }

        private static bool IsRestrictedIpv4(IPAddress ip)
        {
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            byte[] bytes = ip.GetAddressBytes();
            return bytes[0] == 0 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254;
        }

        private bool IsCoordinator(string host)
        {
            if (String.IsNullOrEmpty(host) || !host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return false;
            string label = host.Substring(0, host.Length - domain.Length - 1);
            return IsLabel(label);
        }

        private static bool IsLabel(string label)
        {
            if (String.IsNullOrEmpty(label) || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') return false;
            foreach (char c in label)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-')) return false;
            return true;
        }

        private static bool TrySecureUri(string value, out Uri uri)
        {
            uri = null;
            if (String.IsNullOrEmpty(value)) return false;
            foreach (char c in value) if (Char.IsWhiteSpace(c) || Char.IsControl(c) || c == '\\') return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !uri.IsWellFormedOriginalString()
                || uri.Scheme != Uri.UriSchemeHttps || !String.IsNullOrEmpty(uri.UserInfo)
                || uri.HostNameType == UriHostNameType.Unknown || uri.HostNameType == UriHostNameType.Basic) return false;
            int start = value.IndexOf("://", StringComparison.Ordinal) + 3;
            int end = value.IndexOfAny(new[] { '/', '?', '#' }, start);
            return value.Substring(start, (end < 0 ? value.Length : end) - start).IndexOf('@') < 0;
        }

        private static string Origin(Uri uri)
        {
            return new UriBuilder(Uri.UriSchemeHttps, uri.IdnHost, uri.Port).Uri.GetLeftPart(UriPartial.Authority);
        }

        private static async Task<object> ReadJsonAsync(Uri uri, string body, CancellationToken cancellation)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(5000);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false;
                request.UseDefaultCredentials = false;
                request.Credentials = null;
                request.CookieContainer = null;
                request.Timeout = request.ReadWriteTimeout = 5000;
                request.Accept = "application/json";
                request.Method = body == null ? "GET" : "POST";
                using (timeout.Token.Register(request.Abort))
                {
                    try
                    {
                        if (body != null)
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(body);
                            request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
                            request.ContentLength = bytes.Length;
                            using (Stream upload = await request.GetRequestStreamAsync())
                                await upload.WriteAsync(bytes, 0, bytes.Length, timeout.Token);
                        }
                        using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                        {
                            if (response.StatusCode != HttpStatusCode.OK) throw new IOException("QuickConnect 확인 요청에 정상 응답이 없습니다.");
                            if (response.ContentLength > MaximumResponseBytes) throw new IOException("QuickConnect 응답이 너무 큽니다.");
                            using (Stream stream = response.GetResponseStream())
                            using (MemoryStream buffer = new MemoryStream())
                            {
                                byte[] chunk = new byte[8192];
                                int count;
                                while ((count = await stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token)) != 0)
                                {
                                    if (buffer.Length + count > MaximumResponseBytes) throw new IOException("QuickConnect 응답이 너무 큽니다.");
                                    buffer.Write(chunk, 0, count);
                                }
                                timeout.Token.ThrowIfCancellationRequested();
                                try { return new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes, RecursionLimit = 32 }.DeserializeObject(Encoding.UTF8.GetString(buffer.ToArray())); }
                                catch (ArgumentException) { throw new IOException("QuickConnect 응답 형식이 올바르지 않습니다."); }
                                catch (InvalidOperationException) { throw new IOException("QuickConnect 응답 형식이 올바르지 않습니다."); }
                            }
                        }
                    }
                    catch (WebException)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (timeout.IsCancellationRequested) throw new TimeoutException("QuickConnect 확인 요청 시간이 초과되었습니다.");
                        throw new IOException("QuickConnect 연결 주소를 확인하지 못했습니다. 네트워크와 HTTPS 인증서를 확인해 주세요.");
                    }
                }
            }
        }

        private static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
        private static object Value(Dictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? value : null; }
        private static string Text(Dictionary<string, object> map, string key) { return Value(map, key) as string; }
        private static int Integer(Dictionary<string, object> map, string key)
        {
            int number;
            return Int32.TryParse(Convert.ToString(Value(map, key), CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out number) ? number : -1;
        }
    }
}
