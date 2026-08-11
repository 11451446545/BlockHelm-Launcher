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

using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.App.Animations;
using Launcher.App.Controls;

namespace Launcher.App.Services;

/// <summary>
/// 协调双层内容页的滑动切换以及随页面显示的浮动元素淡入淡出。
/// </summary>
public sealed class SlidingContentTransitionCoordinator
{
    // token 标识当前过渡代次，旧动画结束回调不得修改新页面的可见性。
    private const double MaximumSlideOffset = 48;
    private const double DefaultTransitionScale = 0.985;

    private readonly FrameworkElement loadedElement;
    private readonly FrameworkElement contentHost;
    private readonly FrameworkElement primaryLayer;
    private readonly FrameworkElement secondaryLayer;
    private readonly IReadOnlyList<FrameworkElement> secondaryFloatingElements;
    private readonly bool useSlideTransition;
    private readonly bool useScaleTransition;
    private readonly double transitionScale;
    private readonly ConditionalWeakTable<FrameworkElement, LayerTransforms> layerTransforms = new();
    private bool isSecondaryLayerVisible;
    private bool isTransitionInProgress;
    private int transitionToken;
    private IDisposable? blurRefreshLease;

    public SlidingContentTransitionCoordinator(
        FrameworkElement loadedElement,
        FrameworkElement contentHost,
        FrameworkElement primaryLayer,
        FrameworkElement secondaryLayer,
        IEnumerable<FrameworkElement>? secondaryFloatingElements = null,
        bool useSlideTransition = true,
        bool useScaleTransition = false,
        double transitionScale = DefaultTransitionScale)
    {
        this.loadedElement = loadedElement;
        this.contentHost = contentHost;
        this.primaryLayer = primaryLayer;
        this.secondaryLayer = secondaryLayer;
        this.secondaryFloatingElements = secondaryFloatingElements?.ToArray() ?? [];
        this.useSlideTransition = useSlideTransition;
        this.useScaleTransition = useScaleTransition;
        this.transitionScale = transitionScale;
    }

    public void Sync(bool showSecondaryLayer)
    {
        ReleaseBlurRefreshLease();
        // Sync 用于初始状态或禁用动画场景，先停止全部动画再直接设置稳定终值。
        transitionToken++;
        isSecondaryLayerVisible = showSecondaryLayer;
        isTransitionInProgress = false;

        ResetLayer(primaryLayer, isVisible: !showSecondaryLayer);
        ResetLayer(secondaryLayer, isVisible: showSecondaryLayer);
        SyncFloatingElements(showSecondaryLayer);
    }

    public void AnimateTo(bool showSecondaryLayer)
    {
        // 两层同时移动但方向相反，目标层在动画开始前可见，离开层在完成后折叠。
        if (isSecondaryLayerVisible == showSecondaryLayer)
        {
            if (!isTransitionInProgress)
                Sync(showSecondaryLayer);
            return;
        }

        var shouldAnimateMovement = MotionPreferences.ShouldAnimateMovement;
        var shouldSlide = useSlideTransition && shouldAnimateMovement;
        var shouldScale = useScaleTransition && shouldAnimateMovement;
        if (!loadedElement.IsLoaded || (shouldSlide && contentHost.ActualWidth <= 0))
        {
            Sync(showSecondaryLayer);
            return;
        }

        var previousLayer = isSecondaryLayerVisible ? secondaryLayer : primaryLayer;
        var nextLayer = showSecondaryLayer ? secondaryLayer : primaryLayer;
        var direction = showSecondaryLayer ? 1 : -1;
        var width = Math.Max(contentHost.ActualWidth, 1);
        var slideOffset = Math.Min(width, MaximumSlideOffset);
        var token = ++transitionToken;
        isSecondaryLayerVisible = showSecondaryLayer;
        ReleaseBlurRefreshLease();
        blurRefreshLease = BackdropBlurRefreshCoordinator.BeginContinuousRefresh(previousLayer, nextLayer);

        var previousTransforms = EnsureLayerTransforms(previousLayer);
        var nextTransforms = EnsureLayerTransforms(nextLayer);
        var continueFromCurrentVisuals = isTransitionInProgress;

        var previousOpacity = continueFromCurrentVisuals ? previousLayer.Opacity : 1;
        var previousTranslateX = continueFromCurrentVisuals ? previousTransforms.Translate.X : 0;
        var previousScaleX = continueFromCurrentVisuals ? previousTransforms.Scale.ScaleX : 1;
        var previousScaleY = continueFromCurrentVisuals ? previousTransforms.Scale.ScaleY : 1;
        var nextOpacity = continueFromCurrentVisuals ? nextLayer.Opacity : 0;
        var nextTranslateX = continueFromCurrentVisuals
            ? nextTransforms.Translate.X
            : shouldSlide ? slideOffset * direction : 0;
        var nextScaleX = continueFromCurrentVisuals
            ? nextTransforms.Scale.ScaleX
            : shouldScale ? transitionScale : 1;
        var nextScaleY = continueFromCurrentVisuals
            ? nextTransforms.Scale.ScaleY
            : shouldScale ? transitionScale : 1;

        isTransitionInProgress = true;

        previousLayer.Visibility = Visibility.Visible;
        previousLayer.BeginAnimation(UIElement.OpacityProperty, null);
        previousLayer.Opacity = 0;
        previousTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        previousTransforms.Translate.X = shouldSlide ? -slideOffset * direction : 0;
        previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        previousTransforms.Scale.ScaleX = shouldScale ? transitionScale : 1;
        previousTransforms.Scale.ScaleY = shouldScale ? transitionScale : 1;

        nextLayer.Visibility = Visibility.Visible;
        nextLayer.BeginAnimation(UIElement.OpacityProperty, null);
        nextLayer.Opacity = 1;
        nextTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        nextTransforms.Translate.X = 0;
        nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        nextTransforms.Scale.ScaleX = 1;
        nextTransforms.Scale.ScaleY = 1;

        AnimateFloatingElements(showSecondaryLayer, token);

        var previousSlide = CreateMovementAnimation(previousTranslateX, previousTransforms.Translate.X, MotionDesign.ShortDuration);
        var nextSlide = CreateMovementAnimation(nextTranslateX, 0, MotionDesign.StandardDuration);
        var previousFade = CreateOpacityAnimation(previousOpacity, 0, MotionDesign.ShortDuration);
        var nextFade = CreateOpacityAnimation(nextOpacity, 1, MotionDesign.StandardDuration);
        var previousScaleXAnimation = CreateMovementAnimation(previousScaleX, previousTransforms.Scale.ScaleX, MotionDesign.ShortDuration);
        var previousScaleYAnimation = CreateMovementAnimation(previousScaleY, previousTransforms.Scale.ScaleY, MotionDesign.ShortDuration);
        var nextScaleXAnimation = CreateMovementAnimation(nextScaleX, 1, MotionDesign.StandardDuration);
        var nextScaleYAnimation = CreateMovementAnimation(nextScaleY, 1, MotionDesign.StandardDuration);

        var completionAnimation = shouldSlide ? nextSlide : nextFade;
        completionAnimation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            ResetLayer(previousLayer, isVisible: false);
            ResetLayer(nextLayer, isVisible: true);
            isTransitionInProgress = false;
            ReleaseBlurRefreshLease();
        };

        previousLayer.BeginAnimation(UIElement.OpacityProperty, previousFade, HandoffBehavior.SnapshotAndReplace);
        nextLayer.BeginAnimation(UIElement.OpacityProperty, nextFade, HandoffBehavior.SnapshotAndReplace);
        if (shouldSlide)
        {
            previousTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, previousSlide, HandoffBehavior.SnapshotAndReplace);
            nextTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, nextSlide, HandoffBehavior.SnapshotAndReplace);
        }

        if (shouldScale)
        {
            previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, previousScaleXAnimation, HandoffBehavior.SnapshotAndReplace);
            previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, previousScaleYAnimation, HandoffBehavior.SnapshotAndReplace);
            nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, nextScaleXAnimation, HandoffBehavior.SnapshotAndReplace);
            nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, nextScaleYAnimation, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void ReleaseBlurRefreshLease()
    {
        blurRefreshLease?.Dispose();
        blurRefreshLease = null;
    }

    private void SyncFloatingElements(bool showSecondaryLayer)
    {
        foreach (var element in secondaryFloatingElements)
            ResetFloatingElement(element, showSecondaryLayer);
    }

    private void ResetLayer(FrameworkElement layer, bool isVisible)
    {
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        var transforms = EnsureLayerTransforms(layer);
        transforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        transforms.Translate.X = 0;
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transforms.Scale.ScaleX = 1;
        transforms.Scale.ScaleY = 1;
        layer.Opacity = isVisible ? 1 : 0;
        layer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ResetFloatingElement(FrameworkElement element, bool isVisible)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = isVisible ? 1 : 0;
        element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        element.IsHitTestVisible = isVisible;
    }

    private void AnimateFloatingElements(bool showSecondaryLayer, int token)
    {
        // 浮动元素时长独立于页面滑动，使操作按钮更快响应但仍与目标层一致。
        foreach (var element in secondaryFloatingElements)
        {
            if (showSecondaryLayer)
                FadeFloatingElementIn(element, token);
            else
                FadeFloatingElementOut(element, token);
        }
    }

    private void FadeFloatingElementIn(FrameworkElement element, int token)
    {
        // 开始前清除旧 AnimationClock，并用 token 防止旧 Completed 覆盖新 Opacity。
        var currentOpacity = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = true;
        element.Opacity = 1;

        var animation = CreateFloatingElementFadeAnimation(currentOpacity, 1, isEntering: true);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.Visibility = Visibility.Visible;
            element.IsHitTestVisible = true;
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void FadeFloatingElementOut(FrameworkElement element, int token)
    {
        var currentOpacity = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.IsHitTestVisible = false;
        element.Opacity = 0;

        var animation = CreateFloatingElementFadeAnimation(currentOpacity, 0, isEntering: false);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 0;
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = false;
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateFloatingElementFadeAnimation(double from, double to, bool isEntering)
    {
        var preferredDuration = isEntering ? MotionDesign.ShortDuration : MotionDesign.FastDuration;
        return new DoubleAnimation(from, to, MotionPreferences.ResolveOpacityDuration(preferredDuration))
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };
    }

    private static DoubleAnimation CreateMovementAnimation(double from, double to, Duration duration)
    {
        return new DoubleAnimation(from, to, duration)
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };
    }

    private static DoubleAnimation CreateOpacityAnimation(double from, double to, Duration duration)
    {
        return new DoubleAnimation(from, to, MotionPreferences.ResolveOpacityDuration(duration))
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };
    }

    private LayerTransforms EnsureLayerTransforms(FrameworkElement layer)
    {
        // 在保留模板已有 Transform 的前提下追加 Scale/Translate，并缓存本协调器创建的对象。
        if (useScaleTransition
            && Equals(layer.ReadLocalValue(UIElement.RenderTransformOriginProperty), DependencyProperty.UnsetValue))
        {
            layer.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        if (layerTransforms.TryGetValue(layer, out var existingTransforms)
            && layer.RenderTransform is TransformGroup existingGroup
            && existingGroup.Children.Contains(existingTransforms.Scale)
            && existingGroup.Children.Contains(existingTransforms.Translate))
        {
            return existingTransforms;
        }

        layerTransforms.Remove(layer);
        var scale = new ScaleTransform();
        var translate = new TranslateTransform();
        var group = MotionDesign.EnsureTransformGroup(layer);
        group.Children.Add(scale);
        group.Children.Add(translate);
        var transforms = new LayerTransforms(scale, translate);
        layerTransforms.Add(layer, transforms);
        return transforms;
    }

    private sealed record LayerTransforms(ScaleTransform Scale, TranslateTransform Translate);
}
