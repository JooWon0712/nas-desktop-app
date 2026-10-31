using System;
using System.IO;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal sealed class AppearanceSettings
    {
        public string Theme { get; set; }

        internal static string Read(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    AppearanceSettings settings = new JavaScriptSerializer().Deserialize<AppearanceSettings>(File.ReadAllText(path));
                    string key = CanonicalKey(settings == null ? null : settings.Theme);
                    if (key != null) return key;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return NasTheme.Palettes[0].Key;
        }

        internal static void Save(string path, string key)
        {
            string canonical = CanonicalKey(key);
            if (canonical == null) throw new ArgumentException("알 수 없는 색상 테마입니다.", "key");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(new AppearanceSettings { Theme = canonical }));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static string CanonicalKey(string key)
        {
            foreach (ThemePalette palette in NasTheme.Palettes)
                if (String.Equals(palette.Key, key, StringComparison.OrdinalIgnoreCase)) return palette.Key;
            return null;
        }
    }
}
