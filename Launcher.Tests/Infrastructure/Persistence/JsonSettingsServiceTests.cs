/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class JsonSettingsServiceTests : TestTempDirectory
{
    [Fact]
    public void LauncherSettings_DefaultsUseFixedLauncherBackdrop()
    {
        var settings = new LauncherSettings();

        Assert.Equal(LauncherBackgroundEffects.Gaussian, settings.LauncherBackgroundEffect);
        Assert.Equal(0, settings.LauncherBackgroundOpacityPercent);
        Assert.False(settings.EnableImageBackgroundControlBlur);
    }

    [Theory]
    [InlineData(LauncherBackgroundEffects.None)]
    [InlineData(LauncherBackgroundEffects.Acrylic)]
    [InlineData(LauncherBackgroundEffects.Image)]
    public async Task LoadAsync_MigratesLegacyBackgroundPreferencesToFixedGaussianBackdrop(
        string legacyEffect)
    {
        Directory.CreateDirectory(TempRoot);
        var settingsPath = Path.Combine(TempRoot, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "LauncherBackgroundEffect": "{{legacyEffect}}",
              "LauncherBackgroundOpacityPercent": 85,
              "EnableImageBackgroundControlBlur": true
            }
            """.Replace("{{legacyEffect}}", legacyEffect, StringComparison.Ordinal));

        var settings = await new JsonSettingsService(TempRoot).LoadAsync();

        Assert.Equal(LauncherDefaults.DefaultLauncherBackgroundEffect, settings.LauncherBackgroundEffect);
        Assert.Equal(LauncherDefaults.DefaultLauncherBackgroundOpacityPercent, settings.LauncherBackgroundOpacityPercent);
        Assert.Equal(LauncherDefaults.DefaultEnableImageBackgroundControlBlur, settings.EnableImageBackgroundControlBlur);
    }
}
