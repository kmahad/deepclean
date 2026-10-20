using DeepClean.Core.Configuration;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DeepClean.App.Services;

/// <summary>Applies the saved theme preference to the running application.</summary>
public static class ThemeApplier
{
    public static void Apply(AppThemeMode mode)
    {
        switch (mode)
        {
            case AppThemeMode.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                break;

            case AppThemeMode.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                break;

            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }
}
