using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal sealed class AddressHistoryStore
    {
        private const int MaximumEntries = 30;
        private readonly string path;
        private readonly object gate = new object();

        internal AddressHistoryStore(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("주소 기록 파일 경로를 확인해 주세요.", "path");
            this.path = Path.GetFullPath(path);
        }

        internal IList<ConnectionSettings> Read()
        {
            lock (gate) return ReadEntries();
        }

        internal void Add(ConnectionSettings settings)
        {
            ConnectionSettings normalized = NormalizeOrThrow(settings);
            lock (gate)
            {
                List<ConnectionSettings> entries = ReadEntries();
                entries.RemoveAll(item => Same(item, normalized));
                entries.Insert(0, normalized);
                if (entries.Count > MaximumEntries) entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
                Save(entries);
            }
        }

        internal bool Remove(ConnectionSettings settings)
        {
            ConnectionSettings normalized = NormalizeOrThrow(settings);
            lock (gate)
            {
                List<ConnectionSettings> entries = ReadEntries();
                if (entries.RemoveAll(item => Same(item, normalized)) == 0) return false;
                Save(entries);
                return true;
            }
        }

        private List<ConnectionSettings> ReadEntries()
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (FileNotFoundException) { return new List<ConnectionSettings>(); }
            catch (DirectoryNotFoundException) { return new List<ConnectionSettings>(); }

            object parsed;
            try { parsed = new JavaScriptSerializer().DeserializeObject(json); }
            catch (ArgumentException error) { throw InvalidHistory(error); }
            catch (InvalidOperationException error) { throw InvalidHistory(error); }
            IDictionary<string, object> document = parsed as IDictionary<string, object>;
            object raw;
            if (document == null || !document.TryGetValue("Entries", out raw) || !(raw is object[]))
                throw InvalidHistory(null);

            List<ConnectionSettings> entries = new List<ConnectionSettings>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object value in (object[])raw)
            {
                IDictionary<string, object> item = value as IDictionary<string, object>;
                object url, type;
                if (item == null || !item.TryGetValue("Url", out url) || !item.TryGetValue("Type", out type)
                    || !(url is string) || !(type is string)) continue;
                ConnectionSettings normalized;
                if (!TryNormalize(new ConnectionSettings { Url = (string)url, Type = (string)type }, out normalized)) continue;
                if (!seen.Add(normalized.Type + "\n" + normalized.Url)) continue;
                entries.Add(normalized);
                if (entries.Count == MaximumEntries) break;
            }
            return entries;
        }

        private static ConnectionSettings NormalizeOrThrow(ConnectionSettings settings)
        {
            ConnectionSettings normalized;
            if (!TryNormalize(settings, out normalized))
                throw new ArgumentException("기록할 NAS 주소와 접속 방식을 확인해 주세요.", "settings");
            return normalized;
        }

        private static bool TryNormalize(ConnectionSettings settings, out ConnectionSettings normalized)
        {
            normalized = null;
            if (settings == null || String.IsNullOrEmpty(settings.Type)) return false;
            try { return ConnectionSettings.TryCreate(settings.Url, settings.Kind, out normalized); }
            catch (InvalidDataException) { return false; }
        }

        private static bool Same(ConnectionSettings first, ConnectionSettings second)
        {
            return String.Equals(first.Type, second.Type, StringComparison.Ordinal)
                && String.Equals(first.Url, second.Url, StringComparison.Ordinal);
        }

        private void Save(IList<ConnectionSettings> entries)
        {
            // Serialize only the two address fields, independently of any future
            // settings properties. No browser profile or authentication data lives here.
            List<HistoryEntry> saved = new List<HistoryEntry>();
            foreach (ConnectionSettings entry in entries)
                saved.Add(new HistoryEntry { Url = entry.Url, Type = entry.Type });
            string json = new JavaScriptSerializer().Serialize(new HistoryDocument { Entries = saved });
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { File.Delete(temporary); }
        }

        private static InvalidDataException InvalidHistory(Exception inner)
        {
            return new InvalidDataException("주소 기록 파일 형식을 확인해 주세요.", inner);
        }

        private sealed class HistoryDocument
        {
            public List<HistoryEntry> Entries { get; set; }
        }

        private sealed class HistoryEntry
        {
            public string Url { get; set; }
            public string Type { get; set; }
        }
    }
}
