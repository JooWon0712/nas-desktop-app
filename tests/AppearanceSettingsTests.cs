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
            check(AppearanceSettings.Read(path) == "charcoal-blue", "Missing appearance preferences use the default palette");
            AppearanceSettings.Save(path, "VIOLET");
            AppearanceSettings.Save(path, "amber");
            check(AppearanceSettings.Read(path) == "amber" && !File.Exists(path + ".tmp"), "Theme preference is replaced atomically and read back");
            string saved = File.ReadAllText(path);
            bool rejected = false;
            try { AppearanceSettings.Save(path, "untrusted-theme"); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && File.ReadAllText(path) == saved, "Unknown theme cannot overwrite a valid preference");
            File.WriteAllText(path, "{invalid json");
            check(AppearanceSettings.Read(path) == "charcoal-blue", "Corrupt appearance preferences do not prevent startup");
            File.WriteAllText(path, "{\"Theme\":\"removed-theme\"}");
            check(AppearanceSettings.Read(path) == "charcoal-blue", "Unavailable saved palette falls back safely");
            string current = NasTheme.Current.Key;
            check(!NasTheme.SetPalette("unknown") && NasTheme.Current.Key == current, "Unknown palette leaves the active appearance unchanged");
        }
    }
}
