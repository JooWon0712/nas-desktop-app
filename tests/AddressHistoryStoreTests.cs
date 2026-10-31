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
                onlyAddressFields &= entry.Count == 2 && entry.ContainsKey("Url") && entry.ContainsKey("Type");
            }
            check(onlyAddressFields, "Persisted address history contains only Entries with Url and Type fields");

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
        }

        private static ConnectionSettings Direct(string value)
        {
            ConnectionSettings settings;
            if (!ConnectionSettings.TryCreate(value, ConnectionKind.DirectHttps, out settings)) throw new Exception("Invalid fixture.");
            return settings;
        }
    }
}
