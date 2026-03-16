using Microsoft.UI.Xaml;

namespace CalendarApp.Services;

/// <summary>
/// Applies one of the supported visual themes at runtime.
///
/// For System/Light/Dark themes the built-in ColorPaletteOverride colours are
/// kept; only the root element's RequestedTheme is changed.
///
/// For OS-styled themes (Windows 11, Windows XP, modern macOS, classic macOS X)
/// a custom colour-palette ResourceDictionary is appended to
/// Application.Current.Resources.MergedDictionaries so its ThemeDictionary
/// entries override the default Material colour tokens.  The root element is
/// also forced to ElementTheme.Light because all four palettes are light-mode
/// designs.
/// </summary>
public static class ThemeApplier
{
    // Tracks the currently-injected custom palette so we can remove it on the
    // next theme change.
    private static ResourceDictionary? _activePalette;

    // Maps the DB storage key → (optional palette URI, ElementTheme to apply)
    private static readonly Dictionary<string, (string? PaletteUri, ElementTheme Theme)> ThemeConfig =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["System"]       = (null, ElementTheme.Default),
            ["Light"]        = (null, ElementTheme.Light),
            ["Dark"]         = (null, ElementTheme.Dark),
            ["Windows11"]    = ("ms-appx:///Styles/Themes/Theme_Windows11.xaml",  ElementTheme.Light),
            ["WindowsXP"]    = ("ms-appx:///Styles/Themes/Theme_WindowsXP.xaml",  ElementTheme.Light),
            ["MacOSModern"]  = ("ms-appx:///Styles/Themes/Theme_MacOSModern.xaml", ElementTheme.Light),
            ["MacOSClassic"] = ("ms-appx:///Styles/Themes/Theme_MacOSClassic.xaml", ElementTheme.Light),
        };

    /// <summary>
    /// Applies the theme identified by <paramref name="themeName"/> (the string
    /// stored in the Settings DB, e.g. "Windows11", "Light", "System").
    /// Safe to call from any thread; UI operations are marshalled to the
    /// dispatcher if needed.
    /// </summary>
    public static void Apply(string themeName)
    {
        if (!ThemeConfig.TryGetValue(themeName, out var config))
            config = ThemeConfig["System"];

        var appResources = Application.Current.Resources;

        // ── 1. Remove the previously injected custom palette (if any) ────────
        if (_activePalette != null)
        {
            appResources.MergedDictionaries.Remove(_activePalette);
            _activePalette = null;
        }

        // ── 2. Inject the new custom palette when needed ─────────────────────
        // The new dictionary is appended to the END of MergedDictionaries; in
        // WinUI/Uno the last entry has the highest lookup priority, so our
        // colour tokens override those supplied by MaterialToolkitTheme.
        if (config.PaletteUri != null)
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri(config.PaletteUri)
            };
            appResources.MergedDictionaries.Add(dict);
            _activePalette = dict;
        }

        // ── 3. Set ElementTheme on the window's root content ─────────────────
        // This triggers WinUI/Uno to re-resolve all {ThemeResource ...} bindings
        // with the correct Light / Dark / Default dictionary.
        try
        {
            if (Application.Current is App { MainWindow.Content: FrameworkElement root })
                root.RequestedTheme = config.Theme;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ThemeApplier] Could not set RequestedTheme: {ex.Message}");
        }
    }
}
