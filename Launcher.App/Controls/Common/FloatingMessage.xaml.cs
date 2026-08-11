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
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.App.Animations;

namespace Launcher.App.Controls;

public partial class FloatingMessage : UserControl
{
    private const double EntryOffset = -12;
    private const double ExitOffset = -8;
    private const double EntryScale = 0.975;
    private const double ExitScale = 0.985;
    private int animationToken;

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(FloatingMessage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(FloatingMessage), new PropertyMetadata(false, OnIsOpenChanged));

    public FloatingMessage()
    {
        InitializeComponent();
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FloatingMessage message)
            message.SetOpenState((bool)e.NewValue);
    }

    private void SetOpenState(bool isOpen)
    {
        var token = ++animationToken;
        var wasVisible = Visibility == Visibility.Visible;
        var currentOpacity = Opacity;
        var currentOffset = MessageOffset.Y;
        var currentScale = MessageScale.ScaleX;
        BeginAnimation(OpacityProperty, null);
        MessageOffset.BeginAnimation(TranslateTransform.YProperty, null);
        MessageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MessageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (isOpen)
        {
            Visibility = Visibility.Visible;
            if (!wasVisible)
            {
                currentOpacity = 0;
                currentOffset = MotionPreferences.ShouldAnimateMovement ? EntryOffset : 0;
                currentScale = MotionPreferences.ShouldAnimateMovement ? EntryScale : 1;
            }

            Opacity = 1;
            MessageOffset.Y = 0;
            MessageScale.ScaleX = 1;
            MessageScale.ScaleY = 1;
            AnimateOpacity(
                currentOpacity,
                1,
                MotionPreferences.ResolveOpacityDuration(MotionDesign.StandardDuration));
            if (MotionPreferences.ShouldAnimateMovement)
            {
                AnimateOffset(currentOffset, 0, MotionDesign.StandardDuration);
                AnimateScale(currentScale, 1, MotionDesign.StandardDuration);
            }
            return;
        }

        if (!wasVisible)
            return;

        Opacity = 0;
        var targetOffset = MotionPreferences.ShouldAnimateMovement ? ExitOffset : 0;
        var targetScale = MotionPreferences.ShouldAnimateMovement ? ExitScale : 1;
        MessageOffset.Y = targetOffset;
        MessageScale.ScaleX = targetScale;
        MessageScale.ScaleY = targetScale;
        var fadeOut = CreateAnimation(
            currentOpacity,
            0,
            MotionPreferences.ResolveOpacityDuration(MotionDesign.ShortDuration));
        fadeOut.Completed += (_, _) =>
        {
            if (token != animationToken || IsOpen)
                return;

            Visibility = Visibility.Collapsed;
            Opacity = 0;
            MessageOffset.Y = MotionPreferences.ShouldAnimateMovement ? EntryOffset : 0;
            MessageScale.ScaleX = MotionPreferences.ShouldAnimateMovement ? EntryScale : 1;
            MessageScale.ScaleY = MotionPreferences.ShouldAnimateMovement ? EntryScale : 1;
        };

        BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        if (MotionPreferences.ShouldAnimateMovement)
        {
            AnimateOffset(currentOffset, targetOffset, MotionDesign.ShortDuration);
            AnimateScale(currentScale, targetScale, MotionDesign.ShortDuration);
        }
    }

    private void AnimateOpacity(double from, double to, Duration duration)
    {
        BeginAnimation(
            OpacityProperty,
            CreateAnimation(from, to, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateOffset(double from, double to, Duration duration)
    {
        MessageOffset.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(from, to, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateScale(double from, double to, Duration duration)
    {
        var scaleX = CreateAnimation(from, to, duration);
        var scaleY = CreateAnimation(from, to, duration);
        MessageScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            scaleX,
            HandoffBehavior.SnapshotAndReplace);
        MessageScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleY,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateAnimation(double from, double to, Duration duration)
    {
        return new DoubleAnimation(from, to, duration)
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = MotionDesign.StrongEaseOut
        };
    }
}
