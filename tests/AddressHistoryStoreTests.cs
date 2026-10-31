using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal static class AddressHistoryStoreTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string directory = Path.Combine(Path.GetFullPath(output), "address-history-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "history.json");
            AddressHistoryStore history = new AddressHistoryStore(path);
            ConnectionSettings first = Direct("https://first.history.test/ui/");
            ConnectionSettings second = Direct("https://second.history.test:9443/");
            check(history.Read().Count == 0 && !File.Exists(path) && !history.Remove(first) && !File.Exists(path),
                "Reading or removing an absent history does not create a file or an entry");

            history.Add(first);
            history.Add(second);
            IList<ConnectionSettings> reopened = new AddressHistoryStore(path).Read();
            check(reopened.Count == 2 && reopened[0].Url == second.Url && reopened[1].Url == first.Url,
                "Address history saves only submitted connections and reopens newest first");
            reopened[0].Url = "https://changed-in-memory.history.test/";
            reopened.RemoveAt(1);
            check(history.Read().Count == 2 && history.Read()[0].Url == second.Url,
                "Returned history entries are independent clones rather than mutable stored state");
            history.Add(new ConnectionSettings { Url = "HTTPS://FIRST.HISTORY.TEST:443/ui/", Type = "HTTPS" });
            IList<ConnectionSettings> duplicates = history.Read();
            check(duplicates.Count == 2 && duplicates[0].Url == first.Url && duplicates[0].Type == "https",
                "Adding an equivalent normalized address moves it to the front without duplicating it");
            history.Add(Direct("https://first.history.test/UI/"));
            check(history.Read().Count == 3, "Case-distinct URL paths remain separate history entries");

            ConnectionSettings directQuickConnect = Direct("https://history-demo.quickconnect.to/");
            ConnectionSettings quickConnect;
            if (!ConnectionSettings.TryCreate("history-demo", ConnectionKind.SynologyQuickConnect, out quickConnect)) throw new Exception("Invalid fixture.");
            history.Add(directQuickConnect);
            history.Add(quickConnect);
            check(history.Read()[0].Url == history.Read()[1].Url
                && history.Read()[0].Kind == ConnectionKind.SynologyQuickConnect
                && history.Read()[1].Kind == ConnectionKind.DirectHttps,
                "The same canonical URL retains separate direct HTTPS and QuickConnect history entries");
            check(history.Remove(quickConnect) && history.Read()[0].Kind == ConnectionKind.DirectHttps
                && !history.Remove(quickConnect), "Deleting a history entry preserves another connection type at the same address");
            string afterRemove = File.ReadAllText(path);
            new AddressHistoryStore(path).Read();
            check(File.ReadAllText(path) == afterRemove && !history.Remove(quickConnect),
                "Reopening history does not automatically re-add a deleted address");

            object parsed = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
            IDictionary<string, object> document = (IDictionary<string, object>)parsed;
            object[] serializedEntries = (object[])document["Entries"];
            bool onlyAddressFields = document.Count == 1;
            foreach (object value in serializedEntries)
            {
                IDictionary<string, object> entry = (IDictionary<string, object>)value;
                onlyAddressFields &= entry.Count == 3 && entry.ContainsKey("Url") && entry.ContainsKey("Type") && entry.ContainsKey("Name");
            }
            check(onlyAddressFields, "Persisted address history contains only Entries with Url, Type and Name fields");

            string valid = File.ReadAllText(path);
            bool invalidRejected = true;
            foreach (ConnectionSettings invalid in new[] {
                new ConnectionSettings { Url = "http://invalid.history.test/", Type = "https" },
                new ConnectionSettings { Url = "https://user:fixture-secret@invalid.history.test/", Type = "https" },
                new ConnectionSettings { Url = "unqualified-history-name" },
                new ConnectionSettings { Url = "https://invalid.history.test/", Type = "unknown" }, null })
            {
                bool rejected = false;
                try { history.Add(invalid); }
                catch (ArgumentException) { rejected = true; }
                invalidRejected &= rejected && File.ReadAllText(path) == valid;
            }
            check(invalidRejected, "Invalid or implicit-type connections cannot overwrite valid address history");

            File.WriteAllText(path, "{\"Entries\":[null,5,\"ignored\",{},"
                + "{\"Url\":\"https://valid.history.test/\",\"Type\":\"HTTPS\"},"
                + "{\"Url\":\"http://unsafe.history.test/\",\"Type\":\"https\"},"
                + "{\"Url\":\"https://valid.history.test/\",\"Type\":\"https\"},"
                + "{\"Url\":\"implicit-history-name\"}]}");
            string mixed = File.ReadAllText(path);
            IList<ConnectionSettings> filtered = history.Read();
            check(filtered.Count == 1 && filtered[0].Url == "https://valid.history.test/" && filtered[0].Type == "https"
                && File.ReadAllText(path) == mixed,
                "Malformed individual history entries and duplicates are skipped without rewriting the file");

            bool malformedPreserved = true;
            foreach (string malformed in new[] { "{invalid json", "[]", "null", "{}", "{\"Entries\":null}", "{\"Entries\":{}}" })
            {
                File.WriteAllText(path, malformed);
                bool readRejected = false, addRejected = false;
                try { history.Read(); }
                catch (InvalidDataException) { readRejected = true; }
                try { history.Add(first); }
                catch (InvalidDataException) { addRejected = true; }
                malformedPreserved &= readRejected && addRejected && File.ReadAllText(path) == malformed;
            }
            check(malformedPreserved, "A malformed history document is reported and never silently overwritten by Add");

            string limitPath = Path.Combine(directory, "limited.json");
            AddressHistoryStore limited = new AddressHistoryStore(limitPath);
            for (int index = 0; index < 40; index++) limited.Add(Direct("https://nas-" + index + ".history.test/"));
            IList<ConnectionSettings> recent = limited.Read();
            check(recent.Count == 30 && recent[0].Url == "https://nas-39.history.test/"
                && recent[29].Url == "https://nas-10.history.test/",
                "Address history retains at most thirty newest distinct connections");

            string atomicPath = Path.Combine(directory, "atomic.json");
            AddressHistoryStore atomic = new AddressHistoryStore(atomicPath);
            atomic.Add(first);
            string beforeFailure = File.ReadAllText(atomicPath);
            bool replaceFailed = false;
            using (FileStream held = new FileStream(atomicPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { atomic.Add(second); }
                catch (IOException) { replaceFailed = true; }
                catch (UnauthorizedAccessException) { replaceFailed = true; }
            }
            check(replaceFailed && File.ReadAllText(atomicPath) == beforeFailure
                && Directory.GetFiles(directory, "atomic.json.*.tmp").Length == 0,
                "A failed atomic history replacement preserves the original and removes its temporary file");
            bool readFailed = false;
            using (FileStream held = new FileStream(atomicPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { atomic.Read(); }
                catch (IOException) { readFailed = true; }
            }
            check(readFailed && File.ReadAllText(atomicPath) == beforeFailure,
                "An unreadable history propagates its I/O failure instead of appearing empty");

            RunNamedHistoryTests(directory, first, second, check);
        }

        private static void RunNamedHistoryTests(string directory, ConnectionSettings first, ConnectionSettings second, Action<bool, string> check)
        {
            string path = Path.Combine(directory, "named.json");
            AddressHistoryStore history = new AddressHistoryStore(path);
            check(!history.Rename(first, "없는 NAS") && history.ReadEntries().Count == 0 && !File.Exists(path),
                "Renaming an absent entry does not create history or add the address");

            history.Add(first, "  우리 집 나스  ");
            history.Add(second, new String('가', 60));
            IList<AddressHistoryEntry> named = new AddressHistoryStore(path).ReadEntries();
            check(named.Count == 2 && named[1].Name == "우리 집 나스" && named[1].DisplayName == "우리 집 나스"
                && named[0].Name.Length == 60 && named[1].Connection.Url == first.Url && named[1].Kind == first.Kind,
                "Trimmed Korean NAS names and sixty-character names persist with their connection identity");

            ConnectionSettings detached = named[1].Connection;
            detached.Url = "https://changed-copy.history.test/";
            named[1].Name = "변경한 복사본";
            named[1].Url = "https://changed-entry.history.test/";
            named.Clear();
            check(history.ReadEntries().Count == 2 && history.ReadEntries()[1].Name == "우리 집 나스"
                && history.ReadEntries()[1].Connection.Url == first.Url,
                "Named history entries and their Connection properties are independent clones");

            bool renamed = history.Rename(first, "  작업실 NAS  ");
            named = history.ReadEntries();
            check(renamed && named[0].Url == second.Url && named[1].Url == first.Url && named[1].Name == "작업실 NAS",
                "Renaming changes only the alias without changing connection identity or MRU order");

            history.Add(new ConnectionSettings { Url = "HTTPS://FIRST.HISTORY.TEST:443/ui/", Type = "HTTPS" });
            named = history.ReadEntries();
            check(named.Count == 2 && named[0].Url == first.Url && named[0].Name == "작업실 NAS"
                && named[1].Url == second.Url && named[1].Name.Length == 60,
                "Automatic normalized MRU updates preserve aliases while moving the connection to the front");

            history.Add(first, "새 이름");
            bool cleared = history.Rename(first, "   ");
            named = history.ReadEntries();
            check(cleared && named[0].Name == String.Empty && named[0].DisplayName == "first.history.test"
                && new AddressHistoryEntry().DisplayName == "이름 없는 NAS",
                "Explicit name updates and clearing an alias restore a host-name display fallback");
            history.Add(first, "수동 이름");
            history.Add(first, null);
            check(history.ReadEntries()[0].Name == String.Empty,
                "An explicit Add with an empty name clears a previously stored alias");

            ConnectionSettings directQuickConnect = Direct("https://named-history.quickconnect.to/");
            ConnectionSettings quickConnect;
            if (!ConnectionSettings.TryCreate("named-history", ConnectionKind.SynologyQuickConnect, out quickConnect)) throw new Exception("Invalid fixture.");
            history.Add(directQuickConnect, "직접 연결 NAS");
            history.Add(quickConnect, "퀵커넥트 NAS");
            history.Rename(directQuickConnect, "직접 연결 이름 변경");
            named = history.ReadEntries();
            check(named[0].Kind == ConnectionKind.SynologyQuickConnect && named[0].Name == "퀵커넥트 NAS"
                && named[1].Kind == ConnectionKind.DirectHttps && named[1].Name == "직접 연결 이름 변경"
                && history.Remove(quickConnect) && history.ReadEntries()[0].Name == "직접 연결 이름 변경",
                "Names never merge or rename a different connection type at the same canonical URL");

            string valid = File.ReadAllText(path);
            bool invalidNamesRejected = true;
            foreach (string invalid in new[] { new String('가', 61), "이름\n", "\t이름", "잘못된\0이름", "이름\u007f" })
            {
                bool addRejected = false, renameRejected = false;
                try { history.Add(first, invalid); }
                catch (ArgumentException) { addRejected = true; }
                try { history.Rename(first, invalid); }
                catch (ArgumentException) { renameRejected = true; }
                invalidNamesRejected &= addRejected && renameRejected && File.ReadAllText(path) == valid;
            }
            check(invalidNamesRejected, "Manual Add and Rename reject control characters and long names without modifying history");

            string legacyPath = Path.Combine(directory, "legacy-named.json");
            string legacy = "{\"Entries\":[{\"Url\":\"https://legacy.history.test/ui/\",\"Type\":\"https\"}]}";
            File.WriteAllText(legacyPath, legacy);
            AddressHistoryStore legacyStore = new AddressHistoryStore(legacyPath);
            IList<AddressHistoryEntry> legacyEntries = legacyStore.ReadEntries();
            legacyStore.Read();
            check(legacyEntries.Count == 1 && legacyEntries[0].Name == String.Empty
                && legacyEntries[0].DisplayName == "legacy.history.test" && File.ReadAllText(legacyPath) == legacy,
                "Legacy entries without Name keep their address and host fallback without any read-time migration write");
            legacyStore.Add(legacyEntries[0].Connection);
            check(legacyStore.ReadEntries().Count == 1 && legacyStore.ReadEntries()[0].Name == String.Empty,
                "Reconnecting a legacy unnamed address migrates it without inventing an alias or duplicate");

            List<Dictionary<string, object>> malformedNames = new List<Dictionary<string, object>>();
            object[] rawNames = { null, 42, "제어\n문자", new String('a', 61) };
            for (int index = 0; index < rawNames.Length; index++)
                malformedNames.Add(new Dictionary<string, object> {
                    { "Url", "https://invalid-name-" + index + ".history.test/" }, { "Type", "https" }, { "Name", rawNames[index] }
                });
            string mixedNames = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "Entries", malformedNames } });
            File.WriteAllText(legacyPath, mixedNames);
            IList<AddressHistoryEntry> recovered = legacyStore.ReadEntries();
            bool addressKept = recovered.Count == rawNames.Length;
            foreach (AddressHistoryEntry entry in recovered)
                addressKept &= entry.Name == String.Empty && entry.DisplayName.StartsWith("invalid-name-", StringComparison.Ordinal);
            check(addressKept && File.ReadAllText(legacyPath) == mixedNames,
                "Invalid Name values fall back to empty aliases without dropping valid addresses or rewriting the file");

            string atomicPath = Path.Combine(directory, "atomic-name.json");
            AddressHistoryStore atomic = new AddressHistoryStore(atomicPath);
            atomic.Add(first, "원래 이름");
            string beforeFailure = File.ReadAllText(atomicPath);
            bool renameFailed = false;
            using (FileStream held = new FileStream(atomicPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { atomic.Rename(first, "저장 실패 이름"); }
                catch (IOException) { renameFailed = true; }
                catch (UnauthorizedAccessException) { renameFailed = true; }
            }
            check(renameFailed && File.ReadAllText(atomicPath) == beforeFailure
                && atomic.ReadEntries()[0].Name == "원래 이름" && Directory.GetFiles(directory, "atomic-name.json.*.tmp").Length == 0,
                "A failed atomic rename preserves the old alias and original file and cleans its temporary file");

            File.WriteAllText(atomicPath, "{invalid json");
            bool malformedRead = false, malformedRename = false;
            try { atomic.ReadEntries(); }
            catch (InvalidDataException) { malformedRead = true; }
            try { atomic.Rename(first, "다른 이름"); }
            catch (InvalidDataException) { malformedRename = true; }
            check(malformedRead && malformedRename && File.ReadAllText(atomicPath) == "{invalid json",
                "Named history reads and renames never silently overwrite a malformed document");
        }

        private static ConnectionSettings Direct(string value)
        {
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate(value, ConnectionKind.DirectHttps, out settings)) throw new Exception("Invalid fixture.");
            return settings;
        }
    }
}
