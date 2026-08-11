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

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Animations;

namespace Launcher.App.Behaviors;

/// <summary>
/// Animates a switch template's named thumb transform without relying on template storyboards.
/// </summary>
public static class SwitchThumbAnimation
{
    private const string ThumbTransformName = "ThumbTranslateTransform";
    private const string ThumbElementName = "Thumb";
    private const double DefaultCheckedOffset = 20.25;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SwitchThumbAnimation),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty CheckedOffsetProperty =
        DependencyProperty.RegisterAttached(
            "CheckedOffset",
            typeof(double),
            typeof(SwitchThumbAnimation),
            new PropertyMetadata(DefaultCheckedOffset, OnCheckedOffsetChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(SwitchThumbAnimationState),
            typeof(SwitchThumbAnimation),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    public static double GetCheckedOffset(DependencyObject element)
    {
        return (double)element.GetValue(CheckedOffsetProperty);
    }

    public static void SetCheckedOffset(DependencyObject element, double value)
    {
        element.SetValue(CheckedOffsetProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CheckBox checkBox)
            return;

        if ((bool)e.NewValue)
        {
            if (checkBox.GetValue(StateProperty) is SwitchThumbAnimationState)
                return;

            var state = new SwitchThumbAnimationState(checkBox);
            checkBox.SetValue(StateProperty, state);
            state.Attach();
            return;
        }

        if (checkBox.GetValue(StateProperty) is SwitchThumbAnimationState existingState)
            existingState.Detach();
        checkBox.ClearValue(StateProperty);
    }

    private static void OnCheckedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CheckBox checkBox
            && checkBox.GetValue(StateProperty) is SwitchThumbAnimationState state)
        {
            state.Sync(animate: false);
        }
    }

    private sealed class SwitchThumbAnimationState
    {
        private static readonly DependencyPropertyDescriptor? TemplateDescriptor =
            DependencyPropertyDescriptor.FromProperty(Control.TemplateProperty, typeof(CheckBox));

        private readonly CheckBox checkBox;
        private bool isAttached;
        private bool suppressKeyboardMotion;
        private int keyboardMotionGeneration;
        private int templateSyncGeneration;

        public SwitchThumbAnimationState(CheckBox checkBox)
        {
            this.checkBox = checkBox;
        }

        public void Attach()
        {
            if (isAttached)
                return;

            isAttached = true;
            checkBox.Loaded += CheckBox_Loaded;
            checkBox.Unloaded += CheckBox_Unloaded;
            checkBox.Checked += CheckBox_StateChanged;
            checkBox.Unchecked += CheckBox_StateChanged;
            checkBox.Indeterminate += CheckBox_StateChanged;
            checkBox.PreviewKeyDown += CheckBox_PreviewKeyDown;
            checkBox.PreviewKeyUp += CheckBox_PreviewKeyUp;
            checkBox.LostKeyboardFocus += CheckBox_LostKeyboardFocus;
            checkBox.IsEnabledChanged += CheckBox_IsEnabledChanged;
            TemplateDescriptor?.AddValueChanged(checkBox, CheckBox_TemplateChanged);

            if (checkBox.IsLoaded)
                Sync(animate: false);
        }

        public void Detach()
        {
            if (!isAttached)
                return;

            isAttached = false;
            suppressKeyboardMotion = false;
            keyboardMotionGeneration++;
            templateSyncGeneration++;
            checkBox.Loaded -= CheckBox_Loaded;
            checkBox.Unloaded -= CheckBox_Unloaded;
            checkBox.Checked -= CheckBox_StateChanged;
            checkBox.Unchecked -= CheckBox_StateChanged;
            checkBox.Indeterminate -= CheckBox_StateChanged;
            checkBox.PreviewKeyDown -= CheckBox_PreviewKeyDown;
            checkBox.PreviewKeyUp -= CheckBox_PreviewKeyUp;
            checkBox.LostKeyboardFocus -= CheckBox_LostKeyboardFocus;
            checkBox.IsEnabledChanged -= CheckBox_IsEnabledChanged;
            TemplateDescriptor?.RemoveValueChanged(checkBox, CheckBox_TemplateChanged);
            StopAnimation();
        }

        public void Sync(bool animate)
        {
            if (!isAttached || !checkBox.IsLoaded)
                return;

            checkBox.ApplyTemplate();
            var transform = FindThumbTransform();
            if (transform is null)
                return;

            var target = checkBox.IsChecked == true
                ? GetCheckedOffset(checkBox)
                : 0;
            var current = transform.X;
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = target;

            if (!animate
                || !checkBox.IsEnabled
                || MotionPreferences.IsReducedMotionEnabled
                || Math.Abs(current - target) <= double.Epsilon)
            {
                return;
            }

            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(current, target, MotionDesign.ShortDuration)
                {
                    EasingFunction = MotionDesign.StrongEaseInOut,
                    FillBehavior = FillBehavior.Stop
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        private void CheckBox_Loaded(object sender, RoutedEventArgs e)
        {
            Sync(animate: false);
        }

        private void CheckBox_Unloaded(object sender, RoutedEventArgs e)
        {
            suppressKeyboardMotion = false;
            keyboardMotionGeneration++;
            StopAnimation();
        }

        private void CheckBox_StateChanged(object sender, RoutedEventArgs e)
        {
            Sync(animate: !suppressKeyboardMotion);
        }

        private void CheckBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Space)
                return;

            keyboardMotionGeneration++;
            suppressKeyboardMotion = true;
        }

        private void CheckBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Space)
                return;

            var generation = ++keyboardMotionGeneration;
            checkBox.Dispatcher.BeginInvoke(
                () =>
                {
                    if (isAttached && generation == keyboardMotionGeneration)
                        suppressKeyboardMotion = false;
                },
                DispatcherPriority.Input);
        }

        private void CheckBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            suppressKeyboardMotion = false;
            keyboardMotionGeneration++;
        }

        private void CheckBox_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Sync(animate: false);
        }

        private void CheckBox_TemplateChanged(object? sender, EventArgs e)
        {
            var generation = ++templateSyncGeneration;
            checkBox.Dispatcher.BeginInvoke(
                () =>
                {
                    if (isAttached && generation == templateSyncGeneration)
                        Sync(animate: false);
                },
                DispatcherPriority.Loaded);
        }

        private void StopAnimation()
        {
            FindThumbTransform()?.BeginAnimation(TranslateTransform.XProperty, null);
        }

        private TranslateTransform? FindThumbTransform()
        {
            if (checkBox.Template?.FindName(ThumbTransformName, checkBox) is TranslateTransform namedTransform)
                return namedTransform;

            return checkBox.Template?.FindName(ThumbElementName, checkBox) is FrameworkElement
                {
                    RenderTransform: TranslateTransform fallbackTransform
                }
                ? fallbackTransform
                : null;
        }
    }
}
