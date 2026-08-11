/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class PointerHighlightTests
{
    [Fact]
    public void ResolveSnapshot_FollowsCurrentPointerForNormalMotion()
    {
        var snapshot = PointerHighlight.ResolveSnapshot(
            new Size(160d, 48d),
            new Point(123d, 18d),
            isPressed: false,
            reducedMotion: false);

        Assert.Equal(new Point(123d, 18d), snapshot.Center);
        Assert.Equal(68d, snapshot.Radius);
        Assert.Equal(0.22d, snapshot.Intensity);
    }

    [Fact]
    public void ResolveSnapshot_ContractsAndBrightensForPointerPress()
    {
        var hover = PointerHighlight.ResolveSnapshot(
            new Size(160d, 48d),
            new Point(80d, 24d),
            isPressed: false,
            reducedMotion: false);
        var pressed = PointerHighlight.ResolveSnapshot(
            new Size(160d, 48d),
            new Point(80d, 24d),
            isPressed: true,
            reducedMotion: false);

        Assert.True(pressed.Radius < hover.Radius);
        Assert.True(pressed.Intensity > hover.Intensity);
    }

    [Fact]
    public void ResolveSnapshot_UsesStaticCenteredFeedbackForReducedMotion()
    {
        var snapshot = PointerHighlight.ResolveSnapshot(
            new Size(160d, 48d),
            new Point(150d, 2d),
            isPressed: false,
            reducedMotion: true);

        Assert.Equal(new Point(80d, 24d), snapshot.Center);
        Assert.Equal(0.22d, snapshot.Intensity);
    }

    [Fact]
    public void ResolveSnapshot_ClampsPointerToTheButtonBounds()
    {
        var snapshot = PointerHighlight.ResolveSnapshot(
            new Size(160d, 48d),
            new Point(-4d, 100d),
            isPressed: false,
            reducedMotion: false);

        Assert.Equal(new Point(0d, 48d), snapshot.Center);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void Activation_RequiresAnEnabledButtonAndPointerInteraction(
        bool isEnabled,
        bool isPointerInteraction,
        bool expected)
    {
        Assert.Equal(expected, PointerHighlight.ShouldActivate(isEnabled, isPointerInteraction));
    }

    [Theory]
    [InlineData(typeof(Button), true)]
    [InlineData(typeof(ToggleButton), true)]
    [InlineData(typeof(RepeatButton), false)]
    public void AutomaticRegistration_ExcludesInternalRepeatButtons(Type buttonType, bool expected)
    {
        Assert.Equal(expected, PointerHighlight.ShouldAutoEnable(buttonType));
    }
}
