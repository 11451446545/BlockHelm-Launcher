/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class LiquidGlassEdgeHighlightTests
{
    [Theory]
    [InlineData(80d, 4d, 80d, 0d, 0d, 1d)]
    [InlineData(196d, 40d, 200d, 40d, -1d, 0d)]
    [InlineData(120d, 96d, 120d, 100d, 0d, -1d)]
    [InlineData(3d, 58d, 0d, 58d, 1d, 0d)]
    public void ResolveSnapshot_ProjectsToTheNearestStraightEdge(
        double pointerX,
        double pointerY,
        double expectedX,
        double expectedY,
        double expectedNormalX,
        double expectedNormalY)
    {
        var snapshot = LiquidGlassEdgeHighlight.ResolveSnapshot(
            new Size(200d, 100d),
            new Point(pointerX, pointerY),
            activationDistance: 54d,
            cornerRadius: 16d);

        Assert.Equal(expectedX, snapshot.EdgePoint.X, 6);
        Assert.Equal(expectedY, snapshot.EdgePoint.Y, 6);
        Assert.Equal(expectedNormalX, snapshot.InwardNormal.X, 6);
        Assert.Equal(expectedNormalY, snapshot.InwardNormal.Y, 6);
    }

    [Fact]
    public void ResolveSnapshot_ProjectsOntoRoundedCornerWithAnInwardNormal()
    {
        var snapshot = LiquidGlassEdgeHighlight.ResolveSnapshot(
            new Size(200d, 100d),
            new Point(12d, 12d),
            activationDistance: 54d,
            cornerRadius: 20d);
        var expectedCoordinate = 20d - (20d / Math.Sqrt(2d));

        Assert.Equal(expectedCoordinate, snapshot.EdgePoint.X, 6);
        Assert.Equal(expectedCoordinate, snapshot.EdgePoint.Y, 6);
        Assert.Equal(1d / Math.Sqrt(2d), snapshot.InwardNormal.X, 6);
        Assert.Equal(1d / Math.Sqrt(2d), snapshot.InwardNormal.Y, 6);
    }

    [Fact]
    public void ResolveSnapshot_RemainsContinuousWhereCornerMeetsStraightEdge()
    {
        var beforeJoin = LiquidGlassEdgeHighlight.ResolveSnapshot(
            new Size(200d, 100d),
            new Point(19.9d, 3d),
            activationDistance: 54d,
            cornerRadius: 20d);
        var afterJoin = LiquidGlassEdgeHighlight.ResolveSnapshot(
            new Size(200d, 100d),
            new Point(20.1d, 3d),
            activationDistance: 54d,
            cornerRadius: 20d);

        Assert.True((afterJoin.EdgePoint - beforeJoin.EdgePoint).Length < 0.35d);
        Assert.True(Math.Abs(afterJoin.Intensity - beforeJoin.Intensity) < 0.01d);
    }

    [Fact]
    public void ResolveSnapshot_FadesToZeroAwayFromTheEdge()
    {
        var near = LiquidGlassEdgeHighlight.ResolveSnapshot(
            new Size(200d, 120d),
            new Point(100d, 5d),
            activationDistance: 30d,
            cornerRadius: 16d);
        var far = LiquidGlassEdgeHighlight.ResolveSnapshot(
            new Size(200d, 120d),
            new Point(100d, 60d),
            activationDistance: 30d,
            cornerRadius: 16d);

        Assert.True(near.Intensity > 0.9d);
        Assert.Equal(0d, far.Intensity);
    }

    [Fact]
    public void ResolveSnapshot_ClampsOversizedCornerRadius()
    {
        var snapshot = LiquidGlassEdgeHighlight.ResolveSnapshot(
            new Size(40d, 24d),
            new Point(5d, 5d),
            activationDistance: 20d,
            cornerRadius: 100d);

        Assert.True(double.IsFinite(snapshot.EdgePoint.X));
        Assert.True(double.IsFinite(snapshot.EdgePoint.Y));
        Assert.InRange(snapshot.EdgePoint.X, 0d, 40d);
        Assert.InRange(snapshot.EdgePoint.Y, 0d, 24d);
    }
}
