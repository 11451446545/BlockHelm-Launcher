/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class LauncherBackgroundPresentationPolicyTests
{
    [Theory]
    [InlineData(LauncherBackgroundEffects.None, 100, true)]
    [InlineData(LauncherBackgroundEffects.Image, 85, true)]
    [InlineData("unsupported", -20, false)]
    public void Resolve_AlwaysUsesFixedGaussianBackground(
        string backgroundEffect,
        int preferredOpacityPercent,
        bool enableImageControlBlur)
    {
        var presentation = LauncherBackgroundPresentationPolicy.Resolve(
            backgroundEffect,
            preferredOpacityPercent,
            enableImageControlBlur);

        Assert.Equal(LauncherDefaults.DefaultLauncherBackgroundEffect, presentation.Effect);
        Assert.False(presentation.IsWindowBackdropEnabled);
        Assert.False(presentation.IsImageBackgroundEnabled);
        Assert.False(presentation.IsImageControlBlurEnabled);
        Assert.Equal(LauncherDefaults.DefaultLauncherBackgroundOpacityPercent, presentation.PageBackgroundOpacityPercent);
    }
}
