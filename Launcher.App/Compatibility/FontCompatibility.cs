#if NET6_0
using System.Windows;
using System.Windows.Media;
using Launcher.App.Controls;

namespace Launcher.App.Compatibility;

internal static class FontCompatibility
{
    private const string FontResourceRoot =
        "pack://application:,,,/BlockHelm_Launcher_x64;component/Compatibility/Fonts/";

    // Windows 7 lacks the UI variant of YaHei and the MDL2 glyph font. Keep the
    // original faces when the OS cannot provide them, without editing 91's XAML.
    public static void Apply(ResourceDictionary resources, Window window)
    {
        var installed = Fonts.SystemFontFamilies
            .SelectMany(family => family.FamilyNames.Values)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!installed.Contains("Microsoft YaHei UI"))
        {
            var body = GetEmbeddedFamily("Microsoft YaHei UI");
            resources["LauncherFontFamily"] = body;
            resources["Typography.FontFamily.Body"] = body;
            window.FontFamily = body;
            // The original secondary buttons also have a code-defined font default.
            EventManager.RegisterClassHandler(typeof(SecondaryMenuOptionButton), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is SecondaryMenuOptionButton button && button.GlyphFontFamily.Source == "Microsoft YaHei UI")
                        button.SetCurrentValue(SecondaryMenuOptionButton.GlyphFontFamilyProperty, body);
                }));
        }
        if (!installed.Contains("Segoe MDL2 Assets"))
            resources["LauncherIconFontFamily"] = GetEmbeddedFamily("Segoe MDL2 Assets");
    }

    internal static FontFamily GetEmbeddedFamily(string name)
        => new(new Uri(FontResourceRoot), "./#" + name);
}
#endif
