using System;

namespace NasDesk
{
    internal static class ThemeModeTests
    {
        internal static void Run(Action<bool, string> check)
        {
            string originalKey = NasTheme.Current.Key;
            ThemeMode originalMode = NasTheme.Mode;
            int changes = 0;
            EventHandler changed = delegate { changes++; };
            NasTheme.Changed += changed;
            try
            {
                NasTheme.SetAppearance("forest", ThemeMode.Dark);
                changes = 0;
                NasTheme.SetMode(ThemeMode.Light);
                check(!NasTheme.IsDark && NasTheme.Current.Key == "forest" && changes == 1,
                    "Switching light mode preserves color and raises one appearance update");
                NasTheme.SetMode(ThemeMode.Light);
                check(changes == 1, "Selecting the active mode does not repaint all windows again");
                check(!NasTheme.SetAppearance("unknown", ThemeMode.Dark)
                    && !NasTheme.SetMode((ThemeMode)99) && NasTheme.Mode == ThemeMode.Light
                    && NasTheme.Current.Key == "forest" && changes == 1,
                    "Invalid appearance cannot partially change mode or color");
                NasTheme.SetMode(ThemeMode.System);
                bool targetDark = !NasTheme.IsDark;
                changes = 0;
                NasTheme.RefreshSystemAppearance(targetDark);
                check(NasTheme.Mode == ThemeMode.System && NasTheme.IsDark == targetDark
                    && NasTheme.Current.Key == "forest" && changes == 1,
                    "System preference update changes brightness while preserving color and automatic mode");
                NasTheme.RefreshSystemAppearance(targetDark);
                check(changes == 1, "Repeated system notifications do not duplicate appearance updates");
                NasTheme.SetMode(ThemeMode.Dark);
                changes = 0;
                NasTheme.RefreshSystemAppearance(false);
                check(NasTheme.IsDark && NasTheme.Mode == ThemeMode.Dark && changes == 0,
                    "Explicit dark mode ignores system light preference");
                NasTheme.SetMode(ThemeMode.Light);
                changes = 0;
                NasTheme.RefreshSystemAppearance(true);
                check(!NasTheme.IsDark && NasTheme.Mode == ThemeMode.Light && changes == 0,
                    "Explicit light mode ignores system dark preference");
            }
            finally
            {
                NasTheme.Changed -= changed;
                NasTheme.SetAppearance(originalKey, originalMode);
            }
        }
    }
}
