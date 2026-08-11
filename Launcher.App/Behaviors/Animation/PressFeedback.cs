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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.App.Animations;

namespace Launcher.App.Behaviors;

public static class PressFeedback
{
    private const double PressedScale = 0.97;
    private static readonly Duration PressInDuration = TimeSpan.FromMilliseconds(140);
    private static readonly Duration ReleaseDuration = TimeSpan.FromMilliseconds(100);

    static PressFeedback()
    {
        PointerHighlight.EnsureRegistered();
    }

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PressFeedback),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty FeedbackScaleProperty =
        DependencyProperty.RegisterAttached(
            "FeedbackScale",
            typeof(ScaleTransform),
            typeof(PressFeedback),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Element_PreviewMouseDown), true);
            element.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Element_PreviewMouseUp), true);
            element.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(Element_PreviewTouchDown), true);
            element.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(Element_PreviewTouchUp), true);
            element.MouseLeave += Element_MouseLeave;
            element.LostMouseCapture += Element_LostMouseCapture;
            element.LostTouchCapture += Element_LostTouchCapture;
            element.IsEnabledChanged += Element_IsEnabledChanged;
            element.Unloaded += Element_Unloaded;
            return;
        }

        element.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Element_PreviewMouseDown));
        element.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Element_PreviewMouseUp));
        element.RemoveHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(Element_PreviewTouchDown));
        element.RemoveHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(Element_PreviewTouchUp));
        element.MouseLeave -= Element_MouseLeave;
        element.LostMouseCapture -= Element_LostMouseCapture;
        element.LostTouchCapture -= Element_LostTouchCapture;
        element.IsEnabledChanged -= Element_IsEnabledChanged;
        element.Unloaded -= Element_Unloaded;
        SetScaleImmediately(element, 1);
    }

    private static void Element_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is FrameworkElement { IsEnabled: true } element)
            AnimateScale(element, PressedScale, PressInDuration);
    }

    private static void Element_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is FrameworkElement element)
            AnimateScale(element, 1, ReleaseDuration);
    }

    private static void Element_PreviewTouchDown(object? sender, TouchEventArgs e)
    {
        if (sender is FrameworkElement { IsEnabled: true } element)
            AnimateScale(element, PressedScale, PressInDuration);
    }

    private static void Element_PreviewTouchUp(object? sender, TouchEventArgs e)
    {
        if (sender is FrameworkElement element)
            AnimateScale(element, 1, ReleaseDuration);
    }

    private static void Element_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
            AnimateScale(element, 1, ReleaseDuration);
    }

    private static void Element_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
            AnimateScale(element, 1, ReleaseDuration);
    }

    private static void Element_LostTouchCapture(object? sender, TouchEventArgs e)
    {
        if (sender is FrameworkElement element)
            AnimateScale(element, 1, ReleaseDuration);
    }

    private static void Element_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && e.NewValue is false)
            SetScaleImmediately(element, 1);
    }

    private static void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            SetScaleImmediately(element, 1);
    }

    private static void AnimateScale(FrameworkElement element, double target, Duration duration)
    {
        var scale = EnsureFeedbackScale(element);
        var currentX = scale.ScaleX;
        var currentY = scale.ScaleY;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (MotionPreferences.IsReducedMotionEnabled)
        {
            scale.ScaleX = 1d;
            scale.ScaleY = 1d;
            return;
        }

        scale.ScaleX = target;
        scale.ScaleY = target;
        var animationX = new DoubleAnimation(currentX, target, duration)
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };
        var animationY = new DoubleAnimation(currentY, target, duration)
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animationX, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animationY, HandoffBehavior.SnapshotAndReplace);
    }

    private static void SetScaleImmediately(FrameworkElement element, double target)
    {
        if (element.GetValue(FeedbackScaleProperty) is not ScaleTransform scale)
            return;

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = target;
        scale.ScaleY = target;
    }

    private static ScaleTransform EnsureFeedbackScale(FrameworkElement element)
    {
        if (element.GetValue(FeedbackScaleProperty) is ScaleTransform existingScale
            && element.RenderTransform is TransformGroup existingGroup
            && existingGroup.Children.Contains(existingScale))
        {
            return existingScale;
        }

        var scale = new ScaleTransform();
        MotionDesign.EnsureTransformGroup(element).Children.Add(scale);
        element.SetValue(FeedbackScaleProperty, scale);
        if (Equals(element.ReadLocalValue(UIElement.RenderTransformOriginProperty), DependencyProperty.UnsetValue))
            element.SetCurrentValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));

        return scale;
    }
}
