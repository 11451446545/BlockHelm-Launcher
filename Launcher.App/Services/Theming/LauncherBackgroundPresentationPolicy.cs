/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.App.Services;

internal readonly record struct LauncherBackgroundPresentation(
    string Effect,
    bool IsWindowBackdropEnabled,
    bool IsImageBackgroundEnabled,
    bool IsImageControlBlurEnabled,
    int PageBackgroundOpacityPercent);

internal static class LauncherBackgroundPresentationPolicy
{
    public static LauncherBackgroundPresentation Resolve(
        string? backgroundEffect,
        int preferredOpacityPercent,
        bool enableImageControlBlur)
    {
        return new LauncherBackgroundPresentation(
            LauncherDefaults.DefaultLauncherBackgroundEffect,
            IsWindowBackdropEnabled: false,
            IsImageBackgroundEnabled: false,
            IsImageControlBlurEnabled: false,
            PageBackgroundOpacityPercent: LauncherDefaults.DefaultLauncherBackgroundOpacityPercent);
    }
}
