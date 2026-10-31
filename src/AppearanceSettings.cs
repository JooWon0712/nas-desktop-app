using System;
using System.IO;
using System.Web.Script.Serialization;

namespace NasDesk
{
    internal sealed class AppearanceSettings
    {
        public string Theme { get; set; }
        public string Mode { get; set; }

        internal ThemeMode SelectedMode
        {
            get
            {
                if (String.Equals(Mode, "system", StringComparison.OrdinalIgnoreCase)) return ThemeMode.System;
                if (String.Equals(Mode, "light", StringComparison.OrdinalIgnoreCase)) return ThemeMode.Light;
                return ThemeMode.Dark;
            }
        }

        internal static string Read(string path)
        {
            return ReadPreferences(path).Theme;
        }

        internal static AppearanceSettings ReadPreferences(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    AppearanceSettings settings = new JavaScriptSerializer().Deserialize<AppearanceSettings>(File.ReadAllText(path));
                    string key = CanonicalKey(settings == null ? null : settings.Theme);
                    if (key != null)
                        return new AppearanceSettings { Theme = key, Mode = CanonicalMode(settings.SelectedMode) };
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return new AppearanceSettings { Theme = NasTheme.Palettes[0].Key, Mode = "dark" };
        }

        internal static void Save(string path, string key)
        {
            Save(path, key, NasTheme.Mode);
        }

        internal static void Save(string path, string key, ThemeMode mode)
        {
            string canonical = CanonicalKey(key);
            if (canonical == null) throw new ArgumentException("알 수 없는 색상 테마입니다.", "key");
            string canonicalMode = CanonicalMode(mode);
            if (canonicalMode == null) throw new ArgumentException("알 수 없는 화면 모드입니다.", "mode");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(new AppearanceSettings { Theme = canonical, Mode = canonicalMode }));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static string CanonicalMode(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.System: return "system";
                case ThemeMode.Light: return "light";
                case ThemeMode.Dark: return "dark";
                default: return null;
            }
        }

        private static string CanonicalKey(string key)
        {
            foreach (ThemePalette palette in NasTheme.Palettes)
                if (String.Equals(palette.Key, key, StringComparison.OrdinalIgnoreCase)) return palette.Key;
            return null;
        }
    }
}
