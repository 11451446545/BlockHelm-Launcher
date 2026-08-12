/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Launcher.App.Animations;
using Launcher.App.Controls;

namespace Launcher.App.Behaviors;

/// <summary>
/// Draws a pointer-driven highlight on the nearest part of a rounded surface edge.
/// </summary>
public static class LiquidGlassEdgeHighlight
{
    private const double DefaultActivationDistance = 54d;
    private const double DefaultHighlightLength = 86d;
    private const double DefaultHighlightThickness = 1.35d;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(LiquidGlassEdgeHighlight),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.RegisterAttached(
            "HighlightColor",
            typeof(Color),
            typeof(LiquidGlassEdgeHighlight),
            new FrameworkPropertyMetadata(
                Color.FromArgb(238, 255, 255, 255),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPresentationPropertyChanged));

    public static readonly DependencyProperty ActivationDistanceProperty =
        DependencyProperty.RegisterAttached(
            "ActivationDistance",
            typeof(double),
            typeof(LiquidGlassEdgeHighlight),
            new FrameworkPropertyMetadata(
                DefaultActivationDistance,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPresentationPropertyChanged),
            IsPositiveFinite);

    public static readonly DependencyProperty HighlightLengthProperty =
        DependencyProperty.RegisterAttached(
            "HighlightLength",
            typeof(double),
            typeof(LiquidGlassEdgeHighlight),
            new FrameworkPropertyMetadata(
                DefaultHighlightLength,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPresentationPropertyChanged),
            IsPositiveFinite);

    public static readonly DependencyProperty HighlightThicknessProperty =
        DependencyProperty.RegisterAttached(
            "HighlightThickness",
            typeof(double),
            typeof(LiquidGlassEdgeHighlight),
            new FrameworkPropertyMetadata(
                DefaultHighlightThickness,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPresentationPropertyChanged),
            IsPositiveFinite);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(EdgeHighlightState),
            typeof(LiquidGlassEdgeHighlight),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static Color GetHighlightColor(DependencyObject element) =>
        (Color)element.GetValue(HighlightColorProperty);

    public static void SetHighlightColor(DependencyObject element, Color value) =>
        element.SetValue(HighlightColorProperty, value);

    public static double GetActivationDistance(DependencyObject element) =>
        (double)element.GetValue(ActivationDistanceProperty);

    public static void SetActivationDistance(DependencyObject element, double value) =>
        element.SetValue(ActivationDistanceProperty, value);

    public static double GetHighlightLength(DependencyObject element) =>
        (double)element.GetValue(HighlightLengthProperty);

    public static void SetHighlightLength(DependencyObject element, double value) =>
        element.SetValue(HighlightLengthProperty, value);

    public static double GetHighlightThickness(DependencyObject element) =>
        (double)element.GetValue(HighlightThicknessProperty);

    public static void SetHighlightThickness(DependencyObject element, double value) =>
        element.SetValue(HighlightThicknessProperty, value);

    internal static EdgeHighlightSnapshot ResolveSnapshot(
        Size size,
        Point pointerPosition,
        double activationDistance,
        double cornerRadius)
    {
        if (size.Width <= 0d || size.Height <= 0d || activationDistance <= 0d)
            return default;

        var width = size.Width;
        var height = size.Height;
        var radius = Math.Clamp(cornerRadius, 0d, Math.Min(width, height) / 2d);
        var pointer = new Point(
            Math.Clamp(pointerPosition.X, 0d, width),
            Math.Clamp(pointerPosition.Y, 0d, height));

        var bestPoint = new Point(pointer.X, 0d);
        var bestNormal = new Vector(0d, 1d);
        var bestDistanceSquared = double.PositiveInfinity;

        Consider(new Point(Math.Clamp(pointer.X, radius, width - radius), 0d), new Vector(0d, 1d));
        Consider(new Point(width, Math.Clamp(pointer.Y, radius, height - radius)), new Vector(-1d, 0d));
        Consider(new Point(Math.Clamp(pointer.X, radius, width - radius), height), new Vector(0d, -1d));
        Consider(new Point(0d, Math.Clamp(pointer.Y, radius, height - radius)), new Vector(1d, 0d));

        if (radius > 0d)
        {
            ConsiderArc(new Point(radius, radius), Math.PI, Math.PI * 1.5d);
            ConsiderArc(new Point(width - radius, radius), Math.PI * 1.5d, Math.PI * 2d);
            ConsiderArc(new Point(width - radius, height - radius), 0d, Math.PI * 0.5d);
            ConsiderArc(new Point(radius, height - radius), Math.PI * 0.5d, Math.PI);
        }

        var distance = Math.Sqrt(bestDistanceSquared);
        var proximity = 1d - Math.Clamp(distance / activationDistance, 0d, 1d);
        var intensity = proximity * proximity * (3d - (2d * proximity));
        return new EdgeHighlightSnapshot(bestPoint, bestNormal, distance, intensity);

        void Consider(Point candidate, Vector inwardNormal)
        {
            var delta = candidate - pointer;
            var distanceSquared = delta.LengthSquared;
            if (distanceSquared >= bestDistanceSquared)
                return;

            bestDistanceSquared = distanceSquared;
            bestPoint = candidate;
            bestNormal = inwardNormal;
        }

        void ConsiderArc(Point center, double minimumAngle, double maximumAngle)
        {
            var delta = pointer - center;
            var angle = Math.Atan2(delta.Y, delta.X);
            if (angle < 0d)
                angle += Math.PI * 2d;

            angle = Math.Clamp(angle, minimumAngle, maximumAngle);
            var candidate = new Point(
                center.X + (Math.Cos(angle) * radius),
                center.Y + (Math.Sin(angle) * radius));
            var inwardNormal = center - candidate;
            if (inwardNormal.LengthSquared > double.Epsilon)
                inwardNormal.Normalize();
            Consider(candidate, inwardNormal);
        }
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        if (e.NewValue is true)
        {
            element.MouseEnter += Element_MouseEnter;
            element.MouseMove += Element_MouseMove;
            element.MouseLeave += Element_MouseLeave;
            element.Unloaded += Element_Unloaded;
            return;
        }

        element.MouseEnter -= Element_MouseEnter;
        element.MouseMove -= Element_MouseMove;
        element.MouseLeave -= Element_MouseLeave;
        element.Unloaded -= Element_Unloaded;
        RemoveState(element);
    }

    private static void OnPresentationPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject.GetValue(StateProperty) is EdgeHighlightState state)
            state.RefreshPresentation();
    }

    private static void Element_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && IsRealMouse(e))
            GetOrCreateState(element).Show(e.GetPosition(element));
    }

    private static void Element_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || !IsRealMouse(e))
            return;

        GetOrCreateState(element).Move(e.GetPosition(element));
    }

    private static void Element_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.GetValue(StateProperty) is EdgeHighlightState state)
        {
            state.Hide();
        }
    }

    private static void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            RemoveState(element);
    }

    private static EdgeHighlightState GetOrCreateState(FrameworkElement element)
    {
        if (element.GetValue(StateProperty) is EdgeHighlightState state)
            return state;

        state = new EdgeHighlightState(element);
        element.SetValue(StateProperty, state);
        return state;
    }

    private static void RemoveState(FrameworkElement element)
    {
        if (element.GetValue(StateProperty) is not EdgeHighlightState state)
            return;

        state.Dispose();
        element.ClearValue(StateProperty);
    }

    private static bool IsPositiveFinite(object value)
    {
        var number = (double)value;
        return double.IsFinite(number) && number > 0d;
    }

    private static bool IsRealMouse(MouseEventArgs e) => e.StylusDevice is null;

    internal readonly record struct EdgeHighlightSnapshot(
        Point EdgePoint,
        Vector InwardNormal,
        double Distance,
        double Intensity);

    private sealed class EdgeHighlightState(FrameworkElement element) : IDisposable
    {
        private readonly FrameworkElement element = element;
        private AdornerLayer? layer;
        private EdgeHighlightAdorner? adorner;

        public void Show(Point pointerPosition)
        {
            EnsureAdorner();
            adorner?.Show(pointerPosition);
        }

        public void Move(Point pointerPosition)
        {
            EnsureAdorner();
            adorner?.Move(pointerPosition);
        }

        public void Hide() => adorner?.Hide();

        public void RefreshPresentation() => adorner?.RefreshPresentation();

        public void Dispose()
        {
            if (adorner is not null)
            {
                adorner.Dispose();
                layer?.Remove(adorner);
            }

            adorner = null;
            layer = null;
        }

        private void EnsureAdorner()
        {
            if (adorner is not null || !element.IsLoaded)
                return;

            layer = AdornerLayer.GetAdornerLayer(element);
            if (layer is null)
                return;

            adorner = new EdgeHighlightAdorner(element);
            layer.Add(adorner);
        }
    }

    private sealed class EdgeHighlightAdorner : Adorner, IDisposable
    {
        private const double PositionResponseSeconds = 0.040d;
        private const double IntensityResponseSeconds = 0.030d;
        private const double SettledPositionDistanceSquared = 0.02d;
        private const double SettledIntensityDelta = 0.002d;

        private readonly RadialGradientBrush bloomBrush = CreateGradientBrush();
        private readonly RadialGradientBrush coreBrush = CreateGradientBrush();
        private readonly Pen bloomPen;
        private readonly Pen corePen;
        private Point currentEdgePoint;
        private Point targetEdgePoint;
        private double currentIntensity;
        private double targetIntensity;
        private TimeSpan lastRenderingTime;
        private bool hasCurrentPoint;
        private bool isRendering;

        public EdgeHighlightAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            bloomPen = new Pen(bloomBrush, 2.6d);
            corePen = new Pen(coreBrush, DefaultHighlightThickness);
            RefreshPresentation();
        }

        public void Show(Point pointerPosition)
        {
            UpdateTarget(pointerPosition, showImmediately: true);
        }

        public void Move(Point pointerPosition)
        {
            UpdateTarget(pointerPosition, showImmediately: false);
        }

        public void Hide()
        {
            targetIntensity = 0d;
            if (MotionPreferences.IsReducedMotionEnabled)
            {
                currentIntensity = 0d;
                StopRendering();
                InvalidateVisual();
                return;
            }

            StartRendering();
        }

        public void RefreshPresentation()
        {
            var color = GetHighlightColor(AdornedElement);
            ApplyGradientStops(coreBrush, color, 1d);
            ApplyGradientStops(bloomBrush, color, 0.16d);
            corePen.Thickness = GetHighlightThickness(AdornedElement);
            InvalidateVisual();
        }

        public void Dispose() => StopRendering();

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (currentIntensity <= 0.002d || RenderSize.Width <= 0d || RenderSize.Height <= 0d)
                return;

            var highlightLength = GetHighlightLength(AdornedElement);
            SetGradientGeometry(bloomBrush, currentEdgePoint, highlightLength * 1.22d);
            SetGradientGeometry(coreBrush, currentEdgePoint, highlightLength);

            var thickness = Math.Max(corePen.Thickness, bloomPen.Thickness);
            var inset = thickness / 2d;
            var outlineRect = new Rect(
                inset,
                inset,
                Math.Max(0d, RenderSize.Width - thickness),
                Math.Max(0d, RenderSize.Height - thickness));
            var radius = Math.Max(0d, ResolveCornerRadius(AdornedElement) - inset);

            drawingContext.PushOpacity(Math.Clamp(currentIntensity, 0d, 1d));
            drawingContext.DrawRoundedRectangle(null, bloomPen, outlineRect, radius, radius);
            drawingContext.DrawRoundedRectangle(null, corePen, outlineRect, radius, radius);
            drawingContext.Pop();
        }

        private void UpdateTarget(Point pointerPosition, bool showImmediately)
        {
            var snapshot = ResolveSnapshot(
                AdornedElement.RenderSize,
                pointerPosition,
                GetActivationDistance(AdornedElement),
                ResolveCornerRadius(AdornedElement));
            targetEdgePoint = snapshot.EdgePoint;
            targetIntensity = snapshot.Intensity;

            if (!hasCurrentPoint || MotionPreferences.IsReducedMotionEnabled)
            {
                currentEdgePoint = targetEdgePoint;
                currentIntensity = targetIntensity;
                hasCurrentPoint = true;
                StopRendering();
                InvalidateVisual();
                return;
            }

            if (showImmediately)
                currentIntensity = Math.Max(currentIntensity, targetIntensity * 0.7d);
            StartRendering();
        }

        private void StartRendering()
        {
            if (isRendering)
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
            currentIntensity = Lerp(currentIntensity, targetIntensity, intensityFactor);
            InvalidateVisual();

            if ((targetEdgePoint - currentEdgePoint).LengthSquared <= SettledPositionDistanceSquared
                && Math.Abs(targetIntensity - currentIntensity) <= SettledIntensityDelta)
            {
                currentEdgePoint = targetEdgePoint;
                currentIntensity = targetIntensity;
                StopRendering();
                InvalidateVisual();
            }
        }

        private static double ResolveCornerRadius(UIElement element)
        {
            var radius = element switch
            {
                Border border => MaximumRadius(border.CornerRadius),
                BackdropBlurBorder backdrop => MaximumRadius(backdrop.CornerRadius),
                _ => 0d
            };
            return Math.Clamp(radius, 0d, Math.Min(element.RenderSize.Width, element.RenderSize.Height) / 2d);
        }

        private static double MaximumRadius(CornerRadius radius) =>
            Math.Max(
                Math.Max(radius.TopLeft, radius.TopRight),
                Math.Max(radius.BottomRight, radius.BottomLeft));

        private static RadialGradientBrush CreateGradientBrush()
        {
            return new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                SpreadMethod = GradientSpreadMethod.Pad
            };
        }

        private static void ApplyGradientStops(RadialGradientBrush brush, Color color, double opacityScale)
        {
            brush.GradientStops.Clear();
            brush.GradientStops.Add(new GradientStop(ScaleAlpha(color, opacityScale), 0d));
            brush.GradientStops.Add(new GradientStop(ScaleAlpha(color, opacityScale * 0.82d), 0.2d));
            brush.GradientStops.Add(new GradientStop(ScaleAlpha(color, opacityScale * 0.26d), 0.56d));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1d));
        }

        private static void SetGradientGeometry(RadialGradientBrush brush, Point center, double radius)
        {
            brush.Center = center;
            brush.GradientOrigin = center;
            brush.RadiusX = Math.Max(1d, radius);
            brush.RadiusY = Math.Max(1d, radius);
        }

        private static Color ScaleAlpha(Color color, double scale)
        {
            return Color.FromArgb(
                (byte)Math.Clamp(Math.Round(color.A * scale), 0d, 255d),
                color.R,
                color.G,
                color.B);
        }

        private static double Lerp(double from, double to, double progress) =>
            from + ((to - from) * progress);
    }
}
