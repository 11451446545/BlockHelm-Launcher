/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class ProgressBarAnimationTests
{
    [Fact]
    public void AnimatedProgressIsNormalizedForTransformBindings()
    {
        RunOnStaThread(() =>
        {
            var target = new DependencyObject();

            ProgressBarAnimation.SetAnimatedProgress(target, 0.42d);
            Assert.Equal(0.42d, ProgressBarAnimation.GetAnimatedProgress(target), 6);

            ProgressBarAnimation.SetAnimatedProgress(target, -0.5d);
            Assert.Equal(0d, ProgressBarAnimation.GetAnimatedProgress(target));

            ProgressBarAnimation.SetAnimatedProgress(target, 1.5d);
            Assert.Equal(1d, ProgressBarAnimation.GetAnimatedProgress(target));

            ProgressBarAnimation.SetAnimatedProgress(target, double.NaN);
            Assert.Equal(0d, ProgressBarAnimation.GetAnimatedProgress(target));
        });
    }

    [Fact]
    public void DefaultsStayWithinMotionBudget()
    {
        RunOnStaThread(() =>
        {
            var target = new DependencyObject();

            Assert.InRange(ProgressBarAnimation.GetDurationMilliseconds(target), 0d, 240d);
        });
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
