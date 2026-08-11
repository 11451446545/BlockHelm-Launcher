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
using Launcher.App.Controls;

namespace Launcher.App.Services;

public sealed class DialogOverlayService
{
    private readonly Window owner;
    private int overlayAnimationToken;

    public DialogOverlayService(Window owner)
    {
        this.owner = owner;
    }

    public bool IsSizeAnimating => false;

    public void AnimateSizeChange(DialogHost host, double previousHeight)
    {
        AnimateSizeChange(host.AnimationRoot, previousHeight);
    }

    public void AnimateSizeChange(FrameworkElement dialog, double previousHeight)
    {
        _ = previousHeight;
        dialog.BeginAnimation(FrameworkElement.HeightProperty, null);
        dialog.Height = double.NaN;
        owner.UpdateLayout();
    }

    public void Show(DialogHost host)
    {
        Show(host.OverlayRoot);
    }

    public void Show(Grid overlay)
    {
        var currentOpacity = overlay.Visibility == Visibility.Visible ? overlay.Opacity : 0;
        var token = ++overlayAnimationToken;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Visible;
        overlay.Opacity = 1;

        if (currentOpacity >= 1)
            return;

        var animation = new DoubleAnimation
        {
            From = currentOpacity,
            To = 1,
            Duration = MotionPreferences.ResolveOpacityDuration(MotionDesign.ShortDuration),
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            if (token == overlayAnimationToken)
                overlay.Opacity = 1;
        };
        overlay.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public void Hide(DialogHost host, Action? completed = null)
    {
        Hide(host.OverlayRoot, () =>
        {
            ResetAnimationState(host);
            completed?.Invoke();
        });
    }

    public void ResetAnimationState(DialogHost host)
    {
        ++overlayAnimationToken;

        host.OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);
        host.OverlayRoot.Opacity = host.IsOpen ? 1 : 0;
        host.OverlayRoot.Visibility = host.IsOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void Hide(Grid overlay, Action? completed = null)
    {
        var currentOpacity = overlay.Opacity;
        var token = ++overlayAnimationToken;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);

        overlay.Opacity = currentOpacity;
        if (currentOpacity <= 0)
        {
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentOpacity,
            To = 0,
            Duration = MotionPreferences.ResolveOpacityDuration(MotionDesign.FastDuration),
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            if (token != overlayAnimationToken)
                return;

            overlay.Opacity = 0;
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public void Prewarm(DialogHost host)
    {
        Prewarm(host.OverlayRoot, host.SurfaceBorder);
    }

    public void Prewarm(Grid overlay, Border dialog)
    {
        var originalVisibility = overlay.Visibility;
        var originalOpacity = overlay.Opacity;

        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Hidden;
        overlay.Opacity = 0;

        dialog.ApplyTemplate();
        dialog.UpdateLayout();

        overlay.Visibility = originalVisibility;
        overlay.Opacity = originalOpacity;
    }

}
