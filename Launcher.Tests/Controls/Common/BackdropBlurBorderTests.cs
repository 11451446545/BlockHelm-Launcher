/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Common;

public sealed class BackdropBlurBorderTests
{
    [Fact]
    public void RenderScale_DefaultsToCompatibleLowResolutionValue()
    {
        RunOnStaThread(() =>
        {
            var control = new BackdropBlurBorder();

            Assert.Equal(BackdropBlurBorder.DefaultRenderScale, control.RenderScale);
        });
    }

    [Fact]
    public void RenderScale_UpdatesTemplateBitmapCacheImmediately()
    {
        RunOnStaThread(() =>
        {
            var control = CreateControlWithBlurTemplate();
            control.ApplyTemplate();
            Assert.Equal(BackdropBlurBorder.DefaultRenderScale, control.LocalBlurRenderScale);

            control.RenderScale = 1d;

            Assert.Equal(1d, control.LocalBlurRenderScale);
        });
    }

    [Theory]
    [InlineData(0.09d)]
    [InlineData(1.01d)]
    [InlineData(double.NaN)]
    public void RenderScale_RejectsValuesOutsideSupportedRange(double value)
    {
        RunOnStaThread(() =>
        {
            var control = new BackdropBlurBorder();

            Assert.Throws<ArgumentException>(() => control.RenderScale = value);
        });
    }

    [Theory]
    [InlineData(2, RenderMode.Default, true)]
    [InlineData(1, RenderMode.Default, false)]
    [InlineData(2, RenderMode.SoftwareOnly, false)]
    public void HighFidelityCapability_DisablesBlurForLowTierOrSoftwareRendering(
        int renderingTier,
        RenderMode renderMode,
        bool expected)
    {
        Assert.Equal(
            expected,
            BackdropBlurCapabilities.IsHighFidelitySupported(renderingTier, renderMode));
    }

    [Theory]
    [InlineData(true, 1d, false, false)]
    [InlineData(true, 1d, true, true)]
    [InlineData(true, 0.2d, false, true)]
    [InlineData(false, 1d, true, false)]
    public void HighFidelityGate_DoesNotSilentlyFallBackToLowResolutionBlur(
        bool requested,
        double renderScale,
        bool isSupported,
        bool expected)
    {
        Assert.Equal(
            expected,
            BackdropBlurCapabilities.ShouldEnableBlur(requested, renderScale, isSupported));
    }

    private static BackdropBlurBorder CreateControlWithBlurTemplate()
    {
        var blurLayer = new FrameworkElementFactory(typeof(Border), "PART_BlurLayer");
        blurLayer.SetValue(Border.BackgroundProperty, new VisualBrush());
        blurLayer.SetValue(
            FrameworkElement.CacheModeProperty,
            new BitmapCache { RenderAtScale = BackdropBlurBorder.DefaultRenderScale });

        var template = new ControlTemplate(typeof(BackdropBlurBorder))
        {
            VisualTree = blurLayer
        };
        return new BackdropBlurBorder { Template = template };
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
