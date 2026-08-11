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
using System.Windows.Markup;
using System.Windows.Media.Animation;
using Launcher.App.Animations;
using Launcher.App.Services;

namespace Launcher.App.Controls;

[ContentProperty(nameof(DialogContent))]
public partial class DialogHost : UserControl
{
    private const double DialogVerticalMargin = 48d;

    public static readonly DependencyProperty DialogWidthProperty =
        DependencyProperty.Register(nameof(DialogWidth), typeof(double), typeof(DialogHost), new PropertyMetadata(420d));

    public static readonly DependencyProperty DialogContentProperty =
        DependencyProperty.Register(nameof(DialogContent), typeof(object), typeof(DialogHost), new PropertyMetadata(null));

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(DialogHost), new PropertyMetadata(false, OnIsOpenChanged));

    public static readonly DependencyProperty UseIntegratedOverlayProperty =
        DependencyProperty.Register(
            nameof(UseIntegratedOverlay),
            typeof(bool),
            typeof(DialogHost),
            new PropertyMetadata(false, OnIntegratedOverlayPropertyChanged));

    private DialogOverlayService? integratedOverlayService;
    private Window? ownerWindow;
    private bool suppressIsOpenChanged;
    private int standaloneOverlayAnimationToken;

    public DialogHost()
    {
        InitializeComponent();
        Loaded += DialogHost_Loaded;
        Unloaded += DialogHost_Unloaded;
    }

    public double DialogWidth
    {
        get => (double)GetValue(DialogWidthProperty);
        set => SetValue(DialogWidthProperty, value);
    }

    public object? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool UseIntegratedOverlay
    {
        get => (bool)GetValue(UseIntegratedOverlayProperty);
        set => SetValue(UseIntegratedOverlayProperty, value);
    }

    public bool IsSizeAnimating => integratedOverlayService?.IsSizeAnimating ?? false;

    public Grid OverlayRoot => RootOverlay;

    public Border SurfaceBorder => Surface;

    public FrameworkElement AnimationRoot => DialogChrome;

    public void Show()
    {
        SetIsOpenValue(true);
        if (IsLoaded)
            SetIsOpenCore(true);
    }

    public void Hide(Action? completed = null)
    {
        SetIsOpenValue(false);
        if (IsLoaded)
            SetIsOpenCore(false, completed);
        else
            completed?.Invoke();
    }

    public void Prewarm()
    {
        if (EnsureIntegratedOverlayService())
            integratedOverlayService!.Prewarm(this);
    }

    public void AnimateSizeChange(double previousHeight)
    {
        if (!EnsureIntegratedOverlayService())
            return;

        integratedOverlayService!.AnimateSizeChange(this, previousHeight);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DialogHost host || host.suppressIsOpenChanged || !host.IsLoaded)
            return;

        host.SetIsOpenCore((bool)e.NewValue);
    }

    private static void OnIntegratedOverlayPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DialogHost host || !host.IsLoaded)
            return;

        host.ResetIntegratedOverlayService();
        if (host.UseIntegratedOverlay)
        {
            host.EnsureIntegratedOverlayService();
            if (host.IsOpen)
                host.SetIsOpenCore(true);
        }
    }

    private void DialogHost_Loaded(object sender, RoutedEventArgs e)
    {
        RootOverlay.SizeChanged -= RootOverlay_SizeChanged;
        RootOverlay.SizeChanged += RootOverlay_SizeChanged;
        UpdateDialogChromeMaxHeight();

        if (UseIntegratedOverlay)
        {
            EnsureIntegratedOverlayService();
            Prewarm();
        }

        if (IsOpen)
            SetIsOpenCore(true);
    }

    private void DialogHost_Unloaded(object sender, RoutedEventArgs e)
    {
        RootOverlay.SizeChanged -= RootOverlay_SizeChanged;
        ResetIntegratedOverlayService();
        ResetOverlayVisualState();
    }

    private void SetIsOpenValue(bool value)
    {
        suppressIsOpenChanged = true;
        SetCurrentValue(IsOpenProperty, value);
        suppressIsOpenChanged = false;
    }

    private void SetIsOpenCore(bool isOpen, Action? completed = null)
    {
        UpdateDialogChromeMaxHeight();

        if (UseIntegratedOverlay && EnsureIntegratedOverlayService())
        {
            if (isOpen)
                integratedOverlayService!.Show(this);
            else
                integratedOverlayService!.Hide(this, completed);

            return;
        }

        var currentOpacity = OverlayRoot.Visibility == Visibility.Visible ? OverlayRoot.Opacity : 0;
        var token = ++standaloneOverlayAnimationToken;
        OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);

        if (isOpen)
        {
            OverlayRoot.Visibility = Visibility.Visible;
            OverlayRoot.Opacity = 1;
            if (currentOpacity >= 1)
                return;

            OverlayRoot.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(currentOpacity, 1, MotionPreferences.ResolveOpacityDuration(MotionDesign.ShortDuration))
                {
                    EasingFunction = MotionDesign.StrongEaseOut,
                    FillBehavior = FillBehavior.Stop
                },
                HandoffBehavior.SnapshotAndReplace);
            return;
        }

        if (currentOpacity <= 0 || OverlayRoot.Visibility != Visibility.Visible)
        {
            OverlayRoot.Visibility = Visibility.Collapsed;
            OverlayRoot.Opacity = 0;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(
            currentOpacity,
            0,
            MotionPreferences.ResolveOpacityDuration(MotionDesign.FastDuration))
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (token != standaloneOverlayAnimationToken || IsOpen)
                return;

            OverlayRoot.Visibility = Visibility.Collapsed;
            OverlayRoot.Opacity = 0;
            completed?.Invoke();
        };

        OverlayRoot.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private bool EnsureIntegratedOverlayService()
    {
        if (!UseIntegratedOverlay)
            return false;

        if (integratedOverlayService is not null)
            return true;

        ownerWindow = Window.GetWindow(this);
        if (ownerWindow is null)
            return false;

        integratedOverlayService = new DialogOverlayService(ownerWindow);
        return true;
    }

    private void ResetIntegratedOverlayService()
    {
        integratedOverlayService?.ResetAnimationState(this);

        if (ownerWindow is not null)
        {
            ownerWindow = null;
        }

        integratedOverlayService = null;
    }

    private void RootOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDialogChromeMaxHeight();
    }

    private void UpdateDialogChromeMaxHeight()
    {
        var availableHeight = ActualHeight;
        DialogChrome.MaxHeight = availableHeight > DialogVerticalMargin
            ? availableHeight - DialogVerticalMargin
            : double.PositiveInfinity;
    }

    private void ResetOverlayVisualState()
    {
        standaloneOverlayAnimationToken++;
        OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);
        OverlayRoot.Opacity = 0;
        OverlayRoot.Visibility = Visibility.Collapsed;
    }
}
