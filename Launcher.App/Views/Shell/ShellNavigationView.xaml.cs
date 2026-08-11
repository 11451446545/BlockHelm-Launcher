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
using System.Windows.Threading;
using Launcher.App.Animations;

namespace Launcher.App.Views.Shell;

public partial class ShellNavigationView : UserControl
{
    private int downloadTaskPulseDispatchQueued;
    private bool isDownloadTaskPulseRunning;
    private DateTimeOffset lastDownloadTaskPulseAt = DateTimeOffset.MinValue;
    private MainViewModel? viewModel;

    public ShellNavigationView()
    {
        InitializeComponent();
        DataContextChanged += ShellNavigationView_DataContextChanged;
        Unloaded += (_, _) => AttachViewModel(null);
    }

    private void ShellNavigationView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as MainViewModel);
    }

    private void AttachViewModel(MainViewModel? nextViewModel)
    {
        if (ReferenceEquals(viewModel, nextViewModel))
            return;

        if (viewModel is not null)
            viewModel.DownloadTasksPage.TaskStarted -= DownloadTasksPage_TaskStarted;

        viewModel = nextViewModel;

        if (viewModel is not null)
            viewModel.DownloadTasksPage.TaskStarted += DownloadTasksPage_TaskStarted;
    }

    private void DownloadTasksPage_TaskStarted(object? sender, DownloadTaskItem e)
    {
        if (Interlocked.Exchange(ref downloadTaskPulseDispatchQueued, 1) == 1)
            return;

        Dispatcher.BeginInvoke(TryRunDownloadTaskPulse, DispatcherPriority.Render);
    }

    private void TryRunDownloadTaskPulse()
    {
        Interlocked.Exchange(ref downloadTaskPulseDispatchQueued, 0);

        if (isDownloadTaskPulseRunning)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - lastDownloadTaskPulseAt < TimeSpan.FromMilliseconds(950))
            return;

        lastDownloadTaskPulseAt = now;
        RunDownloadTaskPulse();
    }

    private void RunDownloadTaskPulse()
    {
        const double initialOpacity = 1;
        isDownloadTaskPulseRunning = true;
        var opacityDuration = MotionPreferences.ResolveOpacityDuration(MotionDesign.EmphasizedDuration);
        var movementDuration = MotionPreferences.ResolveMovementDuration(MotionDesign.EmphasizedDuration);

        DownloadTaskPulseCircle.BeginAnimation(UIElement.OpacityProperty, null);
        DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        DownloadTaskPulseCircle.Opacity = initialOpacity;
        DownloadTaskPulseScale.ScaleX = MotionPreferences.ShouldAnimateMovement ? 0.96 : 1;
        DownloadTaskPulseScale.ScaleY = MotionPreferences.ShouldAnimateMovement ? 0.96 : 1;

        var opacityAnimation = new DoubleAnimation(initialOpacity, 0, opacityDuration)
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };
        opacityAnimation.Completed += (_, _) =>
        {
            DownloadTaskPulseCircle.BeginAnimation(UIElement.OpacityProperty, null);
            DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DownloadTaskPulseCircle.Opacity = 0;
            DownloadTaskPulseScale.ScaleX = 1;
            DownloadTaskPulseScale.ScaleY = 1;
            isDownloadTaskPulseRunning = false;
        };

        DownloadTaskPulseCircle.BeginAnimation(
            UIElement.OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);

        if (MotionPreferences.ShouldAnimateMovement)
        {
            var scaleAnimation = new DoubleAnimation(0.96, 1, movementDuration)
            {
                EasingFunction = MotionDesign.StrongEaseOut,
                FillBehavior = FillBehavior.Stop
            };
            DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
            DownloadTaskPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation.Clone(), HandoffBehavior.SnapshotAndReplace);
        }
    }
}
