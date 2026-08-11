/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using Serilog;

namespace Launcher.App.Services;

public static class LauncherWindowBackdrop
{
    private const string BlurFallbackResourceKey = "Brush.LauncherBackground.BlurFallback";
    private static readonly Brush DefaultBlurFallbackBrush = new SolidColorBrush(Color.FromArgb(0xB3, 0x1A, 0x1B, 0x1F));

    public static void Attach(Window window, IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(themeService);

        window.SourceInitialized += (_, _) => Apply(window);
        Apply(window);

        EventHandler<EffectiveThemeChangedEventArgs> themeChangedHandler = (_, _) => Apply(window);
        themeService.EffectiveThemeChanged += themeChangedHandler;
        window.Closed += (_, _) => themeService.EffectiveThemeChanged -= themeChangedHandler;
    }

    private static void Apply(Window window)
    {
        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => ApplyCore(window));
            return;
        }

        ApplyCore(window);
    }

    private static void ApplyCore(Window window)
    {
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is not null)
            chrome.GlassFrameThickness = new Thickness(-1);

        // BlurBehind has no app-supplied color. Theme changes reapply the same
        // neutral policy instead of choosing Acrylic, Mica, or a system backdrop.
        window.Background = System.Windows.Media.Brushes.Transparent;
        var result = NativeBackdrop.ApplyBlurBehind(window);
        if (result is BlurBehindApplyResult.Applied)
        {
            Log.Debug("System BlurBehind applied to launcher window.");
            return;
        }

        window.Background = ResolveBlurFallbackBrush();
        if (result is BlurBehindApplyResult.NoWindowHandle)
        {
            Log.Debug("Launcher window has no HWND yet; using the neutral blur fallback until SourceInitialized.");
            return;
        }

        Log.Warning(
            "System BlurBehind was not applied. Result={BlurBehindResult}. Using the neutral launcher window fallback.",
            result);
    }

    private static Brush ResolveBlurFallbackBrush()
    {
        return global::System.Windows.Application.Current?.TryFindResource(BlurFallbackResourceKey) as Brush
            ?? DefaultBlurFallbackBrush;
    }
}
