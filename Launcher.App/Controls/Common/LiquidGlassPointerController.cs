/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Launcher.App.Animations;
using Launcher.App.Behaviors;
using Launcher.App.Effects;

namespace Launcher.App.Controls;

internal sealed class LiquidGlassPointerController : IDisposable
{
    private const double PositionResponseSeconds = 0.040d;
    private const double IntensityResponseSeconds = 0.030d;
    private readonly BackdropBlurBorder owner;
    private readonly LiquidGlassRefractionEffect effect;
    private UIElement? inputRoot;
    private Point currentEdgePoint;
    private Point targetEdgePoint;
    private Vector currentNormal = new(0d, 1d);
    private Vector targetNormal = new(0d, 1d);
    private double currentIntensity;
    private double targetIntensity;
    private double phase;
    private TimeSpan lastRenderingTime;
    private bool hasCurrentTarget;
    private bool isRendering;
    private bool isDisposed;

    public LiquidGlassPointerController(
        BackdropBlurBorder owner,
        LiquidGlassRefractionEffect effect)
    {
        this.owner = owner;
        this.effect = effect;
    }

    public void Start()
    {
        if (isDisposed || inputRoot is not null || !owner.IsLoaded)
            return;

        inputRoot = Window.GetWindow(owner)
            ?? PresentationSource.FromVisual(owner)?.RootVisual as UIElement;
        if (inputRoot is null)
            return;

        inputRoot.AddHandler(
            Mouse.PreviewMouseMoveEvent,
            new MouseEventHandler(InputRoot_PreviewMouseMove),
            handledEventsToo: true);
        inputRoot.AddHandler(
            Mouse.MouseLeaveEvent,
            new MouseEventHandler(InputRoot_MouseLeave),
            handledEventsToo: true);
        ApplyCurrentState();
    }

    public void Stop()
    {
        if (inputRoot is not null)
        {
            inputRoot.RemoveHandler(
                Mouse.PreviewMouseMoveEvent,
                new MouseEventHandler(InputRoot_PreviewMouseMove));
            inputRoot.RemoveHandler(
                Mouse.MouseLeaveEvent,
                new MouseEventHandler(InputRoot_MouseLeave));
            inputRoot = null;
        }

        targetIntensity = 0d;
        currentIntensity = 0d;
        hasCurrentTarget = false;
        StopRendering();
        ApplyCurrentState();
    }

    public void RefreshConfiguration()
    {
        ApplyCurrentState();
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        Stop();
        isDisposed = true;
    }

    private void InputRoot_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice is not null || !owner.IsVisible || owner.ActualWidth <= 0d || owner.ActualHeight <= 0d)
        {
            SetInactive();
            return;
        }

        Point pointerPosition;
        try
        {
            pointerPosition = e.GetPosition(owner);
        }
        catch (InvalidOperationException)
        {
            SetInactive();
            return;
        }

        var bounds = new Rect(0d, 0d, owner.ActualWidth, owner.ActualHeight);
        if (!bounds.Contains(pointerPosition))
        {
            SetInactive();
            return;
        }

        var snapshot = LiquidGlassEdgeHighlight.ResolveSnapshot(
            bounds.Size,
            pointerPosition,
            owner.LiquidGlassActivationDistance,
            ResolveCornerRadius(owner.CornerRadius));
        SetTarget(snapshot);
    }

    private void InputRoot_MouseLeave(object sender, MouseEventArgs e)
    {
        SetInactive();
    }

    private void SetTarget(LiquidGlassEdgeHighlight.EdgeHighlightSnapshot snapshot)
    {
        var wasInactive = targetIntensity <= 0.002d;
        targetEdgePoint = snapshot.EdgePoint;
        targetNormal = snapshot.InwardNormal;
        targetIntensity = snapshot.Intensity;

        if (!hasCurrentTarget || MotionPreferences.IsReducedMotionEnabled)
        {
            currentEdgePoint = targetEdgePoint;
            currentNormal = targetNormal;
            currentIntensity = targetIntensity;
            hasCurrentTarget = true;
            StopRendering();
            ApplyCurrentState();
            return;
        }

        if (wasInactive && targetIntensity > 0.002d)
        {
            currentEdgePoint = targetEdgePoint;
            currentNormal = targetNormal;
            currentIntensity = Math.Max(currentIntensity, targetIntensity * 0.68d);
        }

        StartRendering();
    }

    private void SetInactive()
    {
        if (targetIntensity <= 0d && currentIntensity <= 0d)
            return;

        targetIntensity = 0d;
        if (MotionPreferences.IsReducedMotionEnabled)
        {
            currentIntensity = 0d;
            StopRendering();
            ApplyCurrentState();
            return;
        }

        StartRendering();
    }

    private void StartRendering()
    {
        if (isRendering || isDisposed)
            return;

        isRendering = true;
        lastRenderingTime = TimeSpan.Zero;
        CompositionTarget.Rendering += CompositionTarget_Rendering;
    }

    private void StopRendering()
    {
        if (!isRendering)
            return;

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        isRendering = false;
        lastRenderingTime = TimeSpan.Zero;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs renderingArgs)
            return;

        var deltaSeconds = lastRenderingTime == TimeSpan.Zero
            ? 1d / 60d
            : Math.Clamp((renderingArgs.RenderingTime - lastRenderingTime).TotalSeconds, 1d / 240d, 0.05d);
        lastRenderingTime = renderingArgs.RenderingTime;

        var positionFactor = 1d - Math.Exp(-deltaSeconds / PositionResponseSeconds);
        var intensityFactor = 1d - Math.Exp(-deltaSeconds / IntensityResponseSeconds);
        currentEdgePoint = new Point(
            Lerp(currentEdgePoint.X, targetEdgePoint.X, positionFactor),
            Lerp(currentEdgePoint.Y, targetEdgePoint.Y, positionFactor));
        currentNormal = new Vector(
            Lerp(currentNormal.X, targetNormal.X, positionFactor),
            Lerp(currentNormal.Y, targetNormal.Y, positionFactor));
        if (currentNormal.LengthSquared > double.Epsilon)
            currentNormal.Normalize();
        currentIntensity = Lerp(currentIntensity, targetIntensity, intensityFactor);
        phase = (phase + (deltaSeconds * 2.8d)) % (Math.PI * 2d);
        ApplyCurrentState();

        var isSettled = (targetEdgePoint - currentEdgePoint).LengthSquared <= 0.02d
            && Math.Abs(targetIntensity - currentIntensity) <= 0.002d;
        if (isSettled)
        {
            currentEdgePoint = targetEdgePoint;
            currentNormal = targetNormal;
            currentIntensity = targetIntensity;
            StopRendering();
            ApplyCurrentState();
        }
    }

    private void ApplyCurrentState()
    {
        var width = Math.Max(owner.ActualWidth, 1d);
        var height = Math.Max(owner.ActualHeight, 1d);
        effect.EdgeX = Math.Clamp(currentEdgePoint.X / width, 0d, 1d);
        effect.EdgeY = Math.Clamp(currentEdgePoint.Y / height, 0d, 1d);
        effect.NormalX = currentNormal.X;
        effect.NormalY = currentNormal.Y;
        effect.AspectRatio = width / height;
        effect.Intensity = Math.Clamp(currentIntensity, 0d, 1d);
        effect.RefractionRadius = Math.Max(owner.LiquidGlassHighlightLength / height, 0.001d);
        effect.DistortionAmount = Math.Max(owner.LiquidGlassDistortion / height, 0d);
        effect.Phase = phase;
        effect.HighlightGain = Math.Max(owner.LiquidGlassHighlightGain, 0d);
        effect.RestingRefraction = Math.Clamp(owner.LiquidGlassRestingRefraction, 0d, 1d);
        effect.CornerRadius = Math.Clamp(ResolveCornerRadius(owner.CornerRadius) / height, 0d, 0.5d);
    }

    private static double ResolveCornerRadius(CornerRadius radius)
    {
        return Math.Max(
            Math.Max(radius.TopLeft, radius.TopRight),
            Math.Max(radius.BottomRight, radius.BottomLeft));
    }

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * progress);
}
