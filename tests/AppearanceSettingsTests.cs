using System;
using System.IO;

namespace NasDesk
{
    internal static class AppearanceSettingsTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string path = Path.Combine(output, "appearance-fixture.json");
            if (File.Exists(path)) File.Delete(path);
            AppearanceSettings missing = AppearanceSettings.ReadPreferences(path);
            check(missing.Theme == "charcoal-blue" && missing.SelectedMode == ThemeMode.Dark,
                "Missing appearance preferences preserve the existing dark default");
            AppearanceSettings.Save(path, "VIOLET");
            AppearanceSettings.Save(path, "amber");
            check(AppearanceSettings.Read(path) == "amber" && !File.Exists(path + ".tmp"), "Theme preference is replaced atomically and read back");
            string saved = File.ReadAllText(path);
            bool rejected = false;
            try { AppearanceSettings.Save(path, "untrusted-theme"); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && File.ReadAllText(path) == saved, "Unknown theme cannot overwrite a valid preference");
            rejected = false;
            try { AppearanceSettings.Save(path, "forest", (ThemeMode)999); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && File.ReadAllText(path) == saved, "Unknown display mode cannot overwrite a valid preference");
            File.WriteAllText(path, "{invalid json");
            AppearanceSettings corrupt = AppearanceSettings.ReadPreferences(path);
            check(corrupt.Theme == "charcoal-blue" && corrupt.SelectedMode == ThemeMode.Dark,
                "Corrupt appearance preferences do not prevent startup");
            File.WriteAllText(path, "{\"Theme\":\"removed-theme\",\"Mode\":\"light\"}");
            AppearanceSettings unavailable = AppearanceSettings.ReadPreferences(path);
            check(unavailable.Theme == "charcoal-blue" && unavailable.SelectedMode == ThemeMode.Dark,
                "Unavailable saved palette falls back safely");
            File.WriteAllText(path, "{\"Theme\":\"forest\"}");
            AppearanceSettings legacy = AppearanceSettings.ReadPreferences(path);
            check(legacy.Theme == "forest" && legacy.SelectedMode == ThemeMode.Dark,
                "Existing theme-only preferences retain their color and dark appearance");
            File.WriteAllText(path, "{\"Theme\":\"SAGE\",\"Mode\":\"LiGhT\"}");
            AppearanceSettings mixedCase = AppearanceSettings.ReadPreferences(path);
            check(mixedCase.Theme == "sage" && mixedCase.Mode == "light" && mixedCase.SelectedMode == ThemeMode.Light,
                "Saved color and mode names are read case-insensitively and canonicalized");
            File.WriteAllText(path, "{\"Theme\":\"forest\",\"Mode\":\"removed-mode\"}");
            AppearanceSettings unknownMode = AppearanceSettings.ReadPreferences(path);
            check(unknownMode.Theme == "forest" && unknownMode.SelectedMode == ThemeMode.Dark,
                "An unavailable display mode preserves a known color with dark fallback");
            foreach (ThemeMode mode in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
            {
                AppearanceSettings.Save(path, "forest", mode);
                AppearanceSettings roundTrip = AppearanceSettings.ReadPreferences(path);
                check(roundTrip.Theme == "forest" && roundTrip.SelectedMode == mode,
                    "Color and " + mode + " display mode survive saving and reopening");
            }
            using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                AppearanceSettings unreadable = AppearanceSettings.ReadPreferences(path);
                check(unreadable.Theme == "charcoal-blue" && unreadable.SelectedMode == ThemeMode.Dark,
                    "Unreadable appearance settings fall back without blocking startup");
            }
            string current = NasTheme.Current.Key;
            check(!NasTheme.SetPalette("unknown") && NasTheme.Current.Key == current, "Unknown palette leaves the active appearance unchanged");
            ThemeMode currentMode = NasTheme.Mode;
            try
            {
                NasTheme.SetAppearance(current, ThemeMode.Light);
                AppearanceSettings.Save(path, "sage");
                AppearanceSettings retainedMode = AppearanceSettings.ReadPreferences(path);
                check(retainedMode.Theme == "sage" && retainedMode.SelectedMode == ThemeMode.Light,
                    "Changing only a color preserves the active display mode in saved settings");
            }
            finally { NasTheme.SetAppearance(current, currentMode); }
        }
    }
}
