/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows.Interop;

namespace Launcher.App.Controls;

/// <summary>
/// Evaluates whether a full-resolution local backdrop can be rendered without
/// silently substituting a low-resolution blur.
/// </summary>
internal static class BackdropBlurCapabilities
{
    internal const int MinimumHighFidelityRenderingTier = 2;

    internal static bool IsHighFidelitySupported(int renderingTier, RenderMode renderMode)
    {
        return renderingTier >= MinimumHighFidelityRenderingTier
            && renderMode is not RenderMode.SoftwareOnly;
    }

    internal static bool ShouldEnableBlur(
        bool isBlurRequested,
        double renderScale,
        bool isHighFidelitySupported)
    {
        return isBlurRequested
            && (renderScale < BackdropBlurBorder.HighFidelityRenderScaleThreshold
                || isHighFidelitySupported);
    }
}
