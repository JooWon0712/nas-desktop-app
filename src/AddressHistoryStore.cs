using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal sealed class AddressHistoryEntry
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Type { get; set; }

        internal ConnectionKind Kind { get { return Connection.Kind; } }
        internal ConnectionSettings Connection { get { return new ConnectionSettings { Url = Url, Type = Type }; } }
        internal string DisplayName
        {
            get
            {
                if (!String.IsNullOrEmpty(Name)) return Name;
                Uri address;
                return Uri.TryCreate(Url, UriKind.Absolute, out address) && !String.IsNullOrEmpty(address.Host)
                    ? address.Host : "이름 없는 NAS";
            }
        }
    }

    internal sealed class AddressHistoryStore
    {
        internal const string ProtectionPurpose = "address-history";
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
            lock (gate)
            {
                List<ConnectionSettings> connections = new List<ConnectionSettings>();
                foreach (AddressHistoryEntry entry in LoadEntries()) connections.Add(entry.Connection);
                return connections;
            }
        }

        internal IList<AddressHistoryEntry> ReadEntries()
        {
            lock (gate) return LoadEntries();
        }

        internal bool ProtectIfNeeded()
        {
            lock (gate)
            {
                string stored;
                try { stored = File.ReadAllText(path); }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                bool wasProtected;
                List<AddressHistoryEntry> entries = ParseEntries(LocalProtectedJson.Decode(stored, ProtectionPurpose, out wasProtected));
                if (wasProtected) return false;
                Save(entries);
                return true;
            }
        }

        internal void Add(ConnectionSettings settings)
        {
            ConnectionSettings normalized = NormalizeOrThrow(settings);
            AddNormalized(normalized, null, false);
        }

        internal void Add(ConnectionSettings settings, string name)
        {
            ConnectionSettings normalized = NormalizeOrThrow(settings);
            AddNormalized(normalized, NormalizeNameOrThrow(name), true);
        }

        private void AddNormalized(ConnectionSettings normalized, string name, bool replaceName)
        {
            lock (gate)
            {
                List<AddressHistoryEntry> entries = LoadEntries();
                AddressHistoryEntry previous = entries.Find(item => Same(item, normalized));
                if (!replaceName) name = previous == null ? String.Empty : previous.Name;
                entries.RemoveAll(item => Same(item, normalized));
                entries.Insert(0, new AddressHistoryEntry { Url = normalized.Url, Type = normalized.Type, Name = name });
                if (entries.Count > MaximumEntries) entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
                Save(entries);
            }
        }

        internal bool Rename(ConnectionSettings settings, string name)
        {
            ConnectionSettings normalized = NormalizeOrThrow(settings);
            string normalizedName = NormalizeNameOrThrow(name);
            lock (gate)
            {
                List<AddressHistoryEntry> entries = LoadEntries();
                AddressHistoryEntry existing = entries.Find(item => Same(item, normalized));
                if (existing == null) return false;
                if (String.Equals(existing.Name, normalizedName, StringComparison.Ordinal)) return true;
                existing.Name = normalizedName;
                Save(entries);
                return true;
            }
        }

        internal bool Remove(ConnectionSettings settings)
        {
            ConnectionSettings normalized = NormalizeOrThrow(settings);
            lock (gate)
            {
                List<AddressHistoryEntry> entries = LoadEntries();
                if (entries.RemoveAll(item => Same(item, normalized)) == 0) return false;
                Save(entries);
                return true;
            }
        }

        private List<AddressHistoryEntry> LoadEntries()
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (FileNotFoundException) { return new List<AddressHistoryEntry>(); }
            catch (DirectoryNotFoundException) { return new List<AddressHistoryEntry>(); }

            return ParseEntries(LocalProtectedJson.Decode(json, ProtectionPurpose));
        }

        private static List<AddressHistoryEntry> ParseEntries(string json)
        {
            object parsed;
            try { parsed = new JavaScriptSerializer().DeserializeObject(json); }
            catch (ArgumentException error) { throw InvalidHistory(error); }
            catch (InvalidOperationException error) { throw InvalidHistory(error); }
            IDictionary<string, object> document = parsed as IDictionary<string, object>;
            object raw;
            if (document == null || !document.TryGetValue("Entries", out raw) || !(raw is object[]))
                throw InvalidHistory(null);

            List<AddressHistoryEntry> entries = new List<AddressHistoryEntry>();
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
                object rawName;
                string name;
                if (!item.TryGetValue("Name", out rawName) || !(rawName is string)
                    || !TryNormalizeName((string)rawName, out name)) name = String.Empty;
                entries.Add(new AddressHistoryEntry { Url = normalized.Url, Type = normalized.Type, Name = name });
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

        private static string NormalizeNameOrThrow(string name)
        {
            string normalized;
            if (!TryNormalizeName(name, out normalized))
                throw new ArgumentException("NAS 이름은 제어 문자 없이 60자 이내로 입력해 주세요.", "name");
            return normalized;
        }

        private static bool TryNormalizeName(string name, out string normalized)
        {
            normalized = String.Empty;
            if (name == null) return true;
            foreach (char character in name)
                if (Char.IsControl(character)) return false;
            string trimmed = name.Trim();
            if (trimmed.Length > 60) return false;
            normalized = trimmed;
            return true;
        }

        private static bool Same(AddressHistoryEntry first, ConnectionSettings second)
        {
            return String.Equals(first.Type, second.Type, StringComparison.Ordinal)
                && String.Equals(first.Url, second.Url, StringComparison.Ordinal);
        }

        private void Save(IList<AddressHistoryEntry> entries)
        {
            // Serialize only the explicit history fields, independently of any future
            // settings properties. No browser profile or authentication data lives here.
            List<HistoryEntry> saved = new List<HistoryEntry>();
            foreach (AddressHistoryEntry entry in entries)
                saved.Add(new HistoryEntry { Url = entry.Url, Type = entry.Type, Name = entry.Name });
            string json = new JavaScriptSerializer().Serialize(new HistoryDocument { Entries = saved });
            LocalProtectedJson.WriteAtomic(path, json, ProtectionPurpose);
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
            public string Name { get; set; }
        }
    }
}
