/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Launcher.App.Animations;

namespace Launcher.App.Behaviors;

public static class ProgressBarAnimation
{
    private const double DefaultDurationMilliseconds = 220d;
    private const double MinimumDurationMilliseconds = 80d;
    private const double MaximumDurationMilliseconds = 240d;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ProgressBarAnimation),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty DurationMillisecondsProperty =
        DependencyProperty.RegisterAttached(
            "DurationMilliseconds",
            typeof(double),
            typeof(ProgressBarAnimation),
            new PropertyMetadata(DefaultDurationMilliseconds));

    public static readonly DependencyProperty AnimatedProgressProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedProgress",
            typeof(double),
            typeof(ProgressBarAnimation),
            new FrameworkPropertyMetadata(0d, null, CoerceAnimatedProgress));

    private static readonly DependencyProperty AnimationVersionProperty =
        DependencyProperty.RegisterAttached(
            "AnimationVersion",
            typeof(int),
            typeof(ProgressBarAnimation),
            new PropertyMetadata(0));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetDurationMilliseconds(DependencyObject element) => (double)element.GetValue(DurationMillisecondsProperty);

    public static void SetDurationMilliseconds(DependencyObject element, double value) => element.SetValue(DurationMillisecondsProperty, value);

    public static double GetAnimatedProgress(DependencyObject element) => (double)element.GetValue(AnimatedProgressProperty);

    public static void SetAnimatedProgress(DependencyObject element, double value) => element.SetValue(AnimatedProgressProperty, value);

    private static int GetAnimationVersion(DependencyObject element) => (int)element.GetValue(AnimationVersionProperty);

    private static void SetAnimationVersion(DependencyObject element, int value) => element.SetValue(AnimationVersionProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProgressBar progressBar)
            return;

        if ((bool)e.NewValue)
        {
            progressBar.Loaded += ProgressBar_Loaded;
            progressBar.Unloaded += ProgressBar_Unloaded;
            progressBar.ValueChanged += ProgressBar_ValueChanged;
            UpdateAnimatedValues(progressBar, animate: false);
            return;
        }

        progressBar.Loaded -= ProgressBar_Loaded;
        progressBar.Unloaded -= ProgressBar_Unloaded;
        progressBar.ValueChanged -= ProgressBar_ValueChanged;
        StopAnimations(progressBar);
    }

    private static void ProgressBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProgressBar progressBar)
            UpdateAnimatedValues(progressBar, animate: false);
    }

    private static void ProgressBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProgressBar progressBar)
            StopAnimations(progressBar);
    }

    private static void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is ProgressBar progressBar)
            UpdateAnimatedValues(progressBar, animate: true);
    }

    private static void UpdateAnimatedValues(ProgressBar progressBar, bool animate)
    {
        var targetProgress = CalculateTargetProgress(progressBar);
        var currentProgress = GetAnimatedProgress(progressBar);

        if (!animate
            || !progressBar.IsLoaded
            || !MotionPreferences.ShouldAnimateMovement
            || targetProgress <= currentProgress
            || Math.Abs(targetProgress - currentProgress) < 0.001d)
        {
            StopAnimations(progressBar);
            SetAnimatedProgress(progressBar, targetProgress);
            return;
        }

        var animationVersion = GetAnimationVersion(progressBar) + 1;
        SetAnimationVersion(progressBar, animationVersion);

        var requestedDurationMilliseconds = GetDurationMilliseconds(progressBar);
        var durationMilliseconds = double.IsFinite(requestedDurationMilliseconds)
            ? Math.Clamp(
                requestedDurationMilliseconds,
                MinimumDurationMilliseconds,
                MaximumDurationMilliseconds)
            : DefaultDurationMilliseconds;
        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);

        SetAnimatedProgress(progressBar, targetProgress);

        var progressAnimation = new DoubleAnimation
        {
            From = currentProgress,
            To = targetProgress,
            Duration = duration,
            FillBehavior = FillBehavior.Stop
        };
        progressAnimation.Completed += (_, _) =>
        {
            if (GetAnimationVersion(progressBar) != animationVersion)
                return;

            progressBar.BeginAnimation(AnimatedProgressProperty, null);
        };

        progressBar.BeginAnimation(AnimatedProgressProperty, progressAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private static double CalculateTargetProgress(ProgressBar progressBar)
    {
        var range = progressBar.Maximum - progressBar.Minimum;
        if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range))
            return 0d;

        var ratio = (progressBar.Value - progressBar.Minimum) / range;
        return double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : 0d;
    }

    private static void StopAnimations(ProgressBar progressBar)
    {
        SetAnimationVersion(progressBar, GetAnimationVersion(progressBar) + 1);
        progressBar.BeginAnimation(AnimatedProgressProperty, null);
    }

    private static object CoerceAnimatedProgress(DependencyObject _, object baseValue)
    {
        var value = (double)baseValue;
        return double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;
    }
}
