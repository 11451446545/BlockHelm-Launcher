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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Serilog;

namespace Launcher.App.Controls;

[TemplatePart(Name = BlurLayerPartName, Type = typeof(Border))]
public sealed class BackdropBlurBorder : ContentControl
{
    internal const string BlurLayerPartName = "PART_BlurLayer";
    private const double BlurOverscanFactor = 1.5d;
    internal const double DefaultRenderScale = 0.2d;
    internal const double MinimumRenderScale = 0.1d;
    internal const double MaximumRenderScale = 1d;
    internal const double HighFidelityRenderScaleThreshold = 0.75d;
    private const double RenderScaleComparisonTolerance = 0.001d;

    public static readonly DependencyProperty SourceElementProperty =
        DependencyProperty.Register(
            nameof(SourceElement),
            typeof(FrameworkElement),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, OnBackdropSourceChanged));

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(
            nameof(BlurRadius),
            typeof(double),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(
                42d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnBlurRadiusChanged),
            IsNonNegativeFiniteDouble);

    public static readonly DependencyProperty IsBlurEnabledProperty =
        DependencyProperty.Register(
            nameof(IsBlurEnabled),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(
                true,
                OnBackdropPresentationChanged,
                CoerceIsBlurEnabled));

    public static readonly DependencyProperty IsSourcePreblurredProperty =
        DependencyProperty.Register(
            nameof(IsSourcePreblurred),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(false, OnIsSourcePreblurredChanged));

    public static readonly DependencyProperty IsTintEnabledProperty =
        DependencyProperty.Register(
            nameof(IsTintEnabled),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty BaseBrushProperty =
        DependencyProperty.Register(
            nameof(BaseBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TintBrushProperty =
        DependencyProperty.Register(
            nameof(TintBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OverlayBrushProperty =
        DependencyProperty.Register(
            nameof(OverlayBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BlurRenderingBiasProperty =
        DependencyProperty.Register(
            nameof(BlurRenderingBias),
            typeof(RenderingBias),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(RenderingBias.Performance, FrameworkPropertyMetadataOptions.AffectsRender),
            IsRenderingBiasValid);

    public static readonly DependencyProperty RenderScaleProperty =
        DependencyProperty.Register(
            nameof(RenderScale),
            typeof(double),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(
                DefaultRenderScale,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRenderScaleChanged),
            IsRenderScaleValid);

    private static readonly DependencyPropertyKey IsHighFidelityBlurSupportedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsHighFidelityBlurSupported),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(false));

    /// <summary>
    /// Exposes the current full-resolution backdrop capability for styles and diagnostics.
    /// A requested high-fidelity blur is disabled, rather than downsampled, when false.
    /// </summary>
    public static readonly DependencyProperty IsHighFidelityBlurSupportedProperty =
        IsHighFidelityBlurSupportedPropertyKey.DependencyProperty;

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(BackdropBlurBorder),
            new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender),
            IsCornerRadiusValid);

    private Border? blurLayer;
    private TileBrush? backdropBrush;
    private BitmapCache? localBlurCache;
    private Rect lastViewbox = Rect.Empty;
    private Rect lastViewport = Rect.Empty;
    private bool isLoaded;
    private bool isRefreshTrackingActive;
    private bool isCapabilityTrackingActive;
    private bool recursiveSourceWarningLogged;
    private bool hasPreparedGeometry;
    private BackdropGeometrySnapshot preparedGeometry;
    private BackdropGeometrySnapshot lastAppliedGeometry;
    private BackdropBlurRefreshCoordinator? refreshCoordinator;
    private ScrollViewer? trackedScrollViewer;

    public BackdropBlurBorder()
    {
        Focusable = false;
        IsTabStop = false;
        Loaded += BackdropBlurBorder_Loaded;
        Unloaded += BackdropBlurBorder_Unloaded;
        IsVisibleChanged += BackdropBlurBorder_IsVisibleChanged;
        SizeChanged += BackdropBlurBorder_SizeChanged;
    }

    public FrameworkElement? SourceElement
    {
        get => (FrameworkElement?)GetValue(SourceElementProperty);
        set => SetValue(SourceElementProperty, value);
    }

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public bool IsBlurEnabled
    {
        get => (bool)GetValue(IsBlurEnabledProperty);
        set => SetValue(IsBlurEnabledProperty, value);
    }

    public bool IsSourcePreblurred
    {
        get => (bool)GetValue(IsSourcePreblurredProperty);
        set => SetValue(IsSourcePreblurredProperty, value);
    }

    public bool IsTintEnabled
    {
        get => (bool)GetValue(IsTintEnabledProperty);
        set => SetValue(IsTintEnabledProperty, value);
    }

    public Brush? BaseBrush
    {
        get => (Brush?)GetValue(BaseBrushProperty);
        set => SetValue(BaseBrushProperty, value);
    }

    public Brush? TintBrush
    {
        get => (Brush?)GetValue(TintBrushProperty);
        set => SetValue(TintBrushProperty, value);
    }

    public Brush? OverlayBrush
    {
        get => (Brush?)GetValue(OverlayBrushProperty);
        set => SetValue(OverlayBrushProperty, value);
    }

    public RenderingBias BlurRenderingBias
    {
        get => (RenderingBias)GetValue(BlurRenderingBiasProperty);
        set => SetValue(BlurRenderingBiasProperty, value);
    }

    /// <summary>
    /// Controls the bitmap cache sampling scale for local Gaussian blur. Values at or
    /// above <see cref="HighFidelityRenderScaleThreshold"/> require hardware support.
    /// </summary>
    public double RenderScale
    {
        get => (double)GetValue(RenderScaleProperty);
        set => SetValue(RenderScaleProperty, value);
    }

    public bool IsHighFidelityBlurSupported =>
        (bool)GetValue(IsHighFidelityBlurSupportedProperty);

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    internal VisualBrush? BackdropBrush => backdropBrush as VisualBrush;

    internal DrawingBrush? BackdropDrawingBrush => backdropBrush as DrawingBrush;

    internal BlurEffect? BackdropEffect => blurLayer?.Effect as BlurEffect;

    internal bool IsBackdropActive => blurLayer?.Visibility == Visibility.Visible;

    internal bool IsRenderTrackingActive => isRefreshTrackingActive;

    internal bool IsRefreshEligible =>
        isLoaded
        && IsVisible
        && IsBlurEnabled
        && SourceElement is not null;

    internal double BlurOverscan => IsSourcePreblurred
        ? 0d
        : CalculateBlurOverscan(BlurRadius);

    internal double LocalBlurRenderScale =>
        localBlurCache?.RenderAtScale ?? 1d;

    internal bool IsUsingDrawingSource =>
        backdropBrush is DrawingBrush { Drawing: not null };

    public override void OnApplyTemplate()
    {
        ClearBackdropSource();

        base.OnApplyTemplate();

        blurLayer = GetTemplateChild(BlurLayerPartName) as Border;
        var templateCache = blurLayer?.CacheMode as BitmapCache;
        localBlurCache = templateCache?.IsFrozen == true
            ? templateCache.CloneCurrentValue()
            : templateCache;
        if (blurLayer is not null
            && localBlurCache is not null
            && !ReferenceEquals(localBlurCache, templateCache))
        {
            blurLayer.CacheMode = localBlurCache;
        }

        var templateBrush = blurLayer?.Background as VisualBrush;
        if (blurLayer is not null && templateBrush is not null)
        {
            backdropBrush = templateBrush.IsFrozen
                ? templateBrush.CloneCurrentValue()
                : templateBrush;
            if (!ReferenceEquals(backdropBrush, templateBrush))
                blurLayer.Background = backdropBrush;
            backdropBrush.ViewboxUnits = BrushMappingMode.Absolute;
            backdropBrush.ViewportUnits = BrushMappingMode.Absolute;
            backdropBrush.TileMode = TileMode.FlipXY;
        }
        else
        {
            backdropBrush = null;
        }
        UpdateBlurLayerOverscan();
        UpdateLocalBlurRenderScale();
        lastViewbox = Rect.Empty;
        lastViewport = Rect.Empty;
        lastAppliedGeometry = default;
        InvalidatePreparedGeometry();
        RequestRefresh(BackdropBlurRefreshReason.Lifecycle);
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        InvalidatePreparedGeometry();
        UpdateRefreshTracking();
        RequestRefresh(BackdropBlurRefreshReason.Layout);
    }

    internal bool RefreshBackdrop()
    {
        if (blurLayer is null || backdropBrush is null)
            return false;

        var geometry = hasPreparedGeometry
            ? preparedGeometry
            : BuildGeometrySnapshot();
        hasPreparedGeometry = false;

        if (!geometry.IsActive)
        {
            if (geometry.HasRecursiveSource && SourceElement is { } recursiveSource)
                LogRecursiveSourceOnce(recursiveSource);
            DeactivateBackdrop(geometry);
            return false;
        }

        UpdateLocalBlurRenderScale();

        EnsureBackdropBrushForSource(geometry.Source!);

        var viewboxChanged = false;
        if (lastViewbox != geometry.Viewbox)
        {
            backdropBrush.Viewbox = geometry.Viewbox;
            lastViewbox = geometry.Viewbox;
            viewboxChanged = true;
        }
        if (lastViewport != geometry.Viewport)
        {
            backdropBrush.Viewport = geometry.Viewport;
            lastViewport = geometry.Viewport;
        }

        lastAppliedGeometry = geometry;
        blurLayer.Visibility = Visibility.Visible;
        return viewboxChanged;
    }

    internal bool PrepareLayoutGeometryRefresh()
    {
        var geometry = BuildGeometrySnapshot();
        if (hasPreparedGeometry && preparedGeometry == geometry)
            return false;

        preparedGeometry = geometry;
        hasPreparedGeometry = true;
        return geometry != lastAppliedGeometry;
    }

    internal void InvalidatePreparedGeometry()
    {
        hasPreparedGeometry = false;
        preparedGeometry = default;
    }

    private static void OnBackdropSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not BackdropBlurBorder border)
            return;

        border.recursiveSourceWarningLogged = false;
        border.lastViewbox = Rect.Empty;
        border.lastViewport = Rect.Empty;
        border.lastAppliedGeometry = default;
        border.InvalidatePreparedGeometry();
        border.UpdateRefreshTracking();
        border.RequestRefresh(BackdropBlurRefreshReason.Source);
    }

    private static void OnBackdropPresentationChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is BackdropBlurBorder border)
        {
            border.InvalidatePreparedGeometry();
            border.UpdateRefreshTracking();
            border.RequestRefresh(BackdropBlurRefreshReason.Lifecycle);
        }
    }

    private static void OnBlurRadiusChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not BackdropBlurBorder border)
            return;

        border.UpdateBlurLayerOverscan();
        border.lastViewbox = Rect.Empty;
        border.lastViewport = Rect.Empty;
        border.lastAppliedGeometry = default;
        border.InvalidatePreparedGeometry();
        border.RequestRefresh(BackdropBlurRefreshReason.Source);
    }

    private static void OnRenderScaleChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not BackdropBlurBorder border)
            return;

        border.UpdateLocalBlurRenderScale();
        border.UpdateHighFidelityBlurSupport();
        border.UpdateCapabilityTracking();
        border.InvalidatePreparedGeometry();
        border.UpdateRefreshTracking();
        border.RequestRefresh(BackdropBlurRefreshReason.Lifecycle);
    }

    private static void OnIsSourcePreblurredChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not BackdropBlurBorder border)
            return;

        border.UpdateBlurLayerOverscan();
        border.lastViewbox = Rect.Empty;
        border.lastViewport = Rect.Empty;
        border.lastAppliedGeometry = default;
        border.InvalidatePreparedGeometry();
        border.UpdateRefreshTracking();
        border.RequestRefresh(BackdropBlurRefreshReason.Source);
    }

    private void BackdropBlurBorder_Loaded(object sender, RoutedEventArgs e)
    {
        isLoaded = true;
        UpdateHighFidelityBlurSupport();
        UpdateCapabilityTracking();
        InvalidatePreparedGeometry();
        UpdateRefreshTracking();
        RequestRefresh(BackdropBlurRefreshReason.Lifecycle);
    }

    private void BackdropBlurBorder_Unloaded(object sender, RoutedEventArgs e)
    {
        isLoaded = false;
        UpdateCapabilityTracking();
        InvalidatePreparedGeometry();
        StopRefreshTracking();
        DeactivateBackdrop();
    }

    private void BackdropBlurBorder_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        InvalidatePreparedGeometry();
        UpdateRefreshTracking();
        RequestRefresh(BackdropBlurRefreshReason.Lifecycle);
    }

    private void BackdropBlurBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        InvalidatePreparedGeometry();
        RequestRefresh(BackdropBlurRefreshReason.Size);
    }

    private void UpdateRefreshTracking()
    {
        if (!IsRefreshEligible)
        {
            StopRefreshTracking();
            DeactivateBackdrop();
            return;
        }

        var coordinator = BackdropBlurRefreshCoordinator.TryGet(this);
        if (coordinator is null)
        {
            StopRefreshTracking();
            return;
        }

        if (!ReferenceEquals(refreshCoordinator, coordinator))
        {
            refreshCoordinator?.Unregister(this);
            refreshCoordinator = coordinator;
        }

        trackedScrollViewer = FindNearestScrollViewer();
        refreshCoordinator.Register(this, SourceElement!, trackedScrollViewer);
        isRefreshTrackingActive = true;
    }

    private bool IsHighFidelityRequested => RenderScale >= HighFidelityRenderScaleThreshold;

    private void UpdateCapabilityTracking()
    {
        var shouldTrack = isLoaded && IsHighFidelityRequested;
        if (shouldTrack == isCapabilityTrackingActive)
            return;

        if (shouldTrack)
            RenderCapability.TierChanged += RenderCapability_TierChanged;
        else
            RenderCapability.TierChanged -= RenderCapability_TierChanged;

        isCapabilityTrackingActive = shouldTrack;
    }

    private void RenderCapability_TierChanged(object? sender, EventArgs e)
    {
        if (!IsHighFidelityRequested)
            return;

        UpdateHighFidelityBlurSupport();
        InvalidatePreparedGeometry();
        UpdateRefreshTracking();
        RequestRefresh(BackdropBlurRefreshReason.Lifecycle);
    }

    private void UpdateHighFidelityBlurSupport()
    {
        var renderingTier = RenderCapability.Tier >> 16;
        var isSupported = BackdropBlurCapabilities.IsHighFidelitySupported(
            renderingTier,
            RenderOptions.ProcessRenderMode);
        SetValue(IsHighFidelityBlurSupportedPropertyKey, isSupported);
        CoerceValue(IsBlurEnabledProperty);
    }

    private void StopRefreshTracking()
    {
        refreshCoordinator?.Unregister(this);
        refreshCoordinator = null;
        trackedScrollViewer = null;
        isRefreshTrackingActive = false;
    }

    private ScrollViewer? FindNearestScrollViewer()
    {
        DependencyObject? current = this;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;
        }

        return null;
    }

    private BackdropGeometrySnapshot BuildGeometrySnapshot()
    {
        if (blurLayer is null
            || backdropBrush is null
            || !IsBlurEnabled
            || !IsVisible
            || ActualWidth <= 0d
            || ActualHeight <= 0d
            || SourceElement is not { } source
            || source.ActualWidth <= 0d
            || source.ActualHeight <= 0d)
        {
            return default;
        }

        if (ReferenceEquals(source, this) || source.IsAncestorOf(this))
            return BackdropGeometrySnapshot.Inactive(source, hasRecursiveSource: true);

        Rect desiredViewbox;
        try
        {
            var overscan = BlurOverscan;
            desiredViewbox = TransformToVisual(source).TransformBounds(
                new Rect(
                    -overscan,
                    -overscan,
                    ActualWidth + (overscan * 2d),
                    ActualHeight + (overscan * 2d)));
        }
        catch (InvalidOperationException)
        {
            return BackdropGeometrySnapshot.Inactive(source);
        }

        if (!IsValidViewbox(desiredViewbox))
            return BackdropGeometrySnapshot.Inactive(source);

        var viewbox = Rect.Intersect(
            desiredViewbox,
            new Rect(0d, 0d, source.ActualWidth, source.ActualHeight));
        viewbox = ClipToScrollViewport(viewbox, source);
        if (!IsValidViewbox(viewbox))
            return BackdropGeometrySnapshot.Inactive(source);

        var overscanSize = BlurOverscan * 2d;
        var viewport = CalculateMirroredViewport(
            desiredViewbox,
            viewbox,
            ActualWidth + overscanSize,
            ActualHeight + overscanSize);
        if (!IsValidViewbox(viewport))
            return BackdropGeometrySnapshot.Inactive(source);

        return new BackdropGeometrySnapshot(
            source,
            viewbox,
            viewport,
            IsActive: true,
            HasRecursiveSource: false);
    }

    private Rect ClipToScrollViewport(Rect viewbox, FrameworkElement source)
    {
        var scrollViewer = trackedScrollViewer;
        if (scrollViewer is null
            || scrollViewer.ActualWidth <= 0d
            || scrollViewer.ActualHeight <= 0d)
        {
            return viewbox;
        }

        try
        {
            var viewportInSource = scrollViewer.TransformToVisual(source).TransformBounds(
                new Rect(0d, 0d, scrollViewer.ActualWidth, scrollViewer.ActualHeight));
            return Rect.Intersect(viewbox, viewportInSource);
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    private void RequestRefresh(BackdropBlurRefreshReason reason)
    {
        if (isRefreshTrackingActive && refreshCoordinator is not null)
            refreshCoordinator.RequestRefresh(this, reason);
    }

    private void UpdateBlurLayerOverscan()
    {
        if (blurLayer is null)
            return;

        var overscan = BlurOverscan;
        blurLayer.Margin = new Thickness(-overscan);
    }

    private void UpdateLocalBlurRenderScale()
    {
        if (localBlurCache is not { } cache)
            return;

        if (Math.Abs(cache.RenderAtScale - RenderScale) > RenderScaleComparisonTolerance)
            cache.RenderAtScale = RenderScale;
    }

    private void EnsureBackdropBrushForSource(FrameworkElement source)
    {
        if (blurLayer is null)
            return;

        var visualBrush = backdropBrush as VisualBrush;
        if (visualBrush is null || !ReferenceEquals(visualBrush.Visual, source))
        {
            visualBrush = CreateBackdropBrush<VisualBrush>();
            visualBrush.Visual = source;
            ReplaceBackdropBrush(visualBrush);
        }
    }

    private void ReplaceBackdropBrush(TileBrush replacement)
    {
        ClearBackdropSource();
        backdropBrush = replacement;
        if (blurLayer is not null)
        {
            blurLayer.Background = replacement;
        }
        lastViewbox = Rect.Empty;
        lastViewport = Rect.Empty;
    }

    private static TBrush CreateBackdropBrush<TBrush>()
        where TBrush : TileBrush, new()
    {
        var brush = new TBrush
        {
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            Stretch = Stretch.Fill,
            TileMode = TileMode.FlipXY,
            ViewboxUnits = BrushMappingMode.Absolute,
            ViewportUnits = BrushMappingMode.Absolute
        };
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        RenderOptions.SetCacheInvalidationThresholdMinimum(brush, 0.5d);
        RenderOptions.SetCacheInvalidationThresholdMaximum(brush, 2d);
        return brush;
    }

    private void ClearBackdropSource()
    {
        switch (backdropBrush)
        {
            case VisualBrush visualBrush:
                visualBrush.Visual = null;
                break;
            case DrawingBrush drawingBrush:
                drawingBrush.Drawing = null;
                break;
        }
    }

    private void DeactivateBackdrop(BackdropGeometrySnapshot geometry = default)
    {
        ClearBackdropSource();

        InvalidatePreparedGeometry();
        lastAppliedGeometry = geometry;
        lastViewbox = Rect.Empty;
        lastViewport = Rect.Empty;
        if (blurLayer is not null)
            blurLayer.Visibility = Visibility.Collapsed;
    }

    private void LogRecursiveSourceOnce(FrameworkElement source)
    {
        if (recursiveSourceWarningLogged)
            return;

        recursiveSourceWarningLogged = true;
        Log.Warning(
            "Backdrop blur source contains the blur control and cannot be sampled safely. SourceType={SourceType}",
            source.GetType().FullName);
    }

    private static bool IsValidViewbox(Rect viewbox)
    {
        return !viewbox.IsEmpty
            && double.IsFinite(viewbox.X)
            && double.IsFinite(viewbox.Y)
            && double.IsFinite(viewbox.Width)
            && double.IsFinite(viewbox.Height)
            && viewbox.Width > 0d
            && viewbox.Height > 0d;
    }

    private static Rect CalculateMirroredViewport(
        Rect desiredViewbox,
        Rect clippedViewbox,
        double destinationWidth,
        double destinationHeight)
    {
        var scaleX = destinationWidth / desiredViewbox.Width;
        var scaleY = destinationHeight / desiredViewbox.Height;
        return new Rect(
            Math.Max(0d, (clippedViewbox.Left - desiredViewbox.Left) * scaleX),
            Math.Max(0d, (clippedViewbox.Top - desiredViewbox.Top) * scaleY),
            Math.Min(destinationWidth, clippedViewbox.Width * scaleX),
            Math.Min(destinationHeight, clippedViewbox.Height * scaleY));
    }

    private static bool IsNonNegativeFiniteDouble(object value)
    {
        var number = (double)value;
        return double.IsFinite(number) && number >= 0d;
    }

    private static bool IsRenderScaleValid(object value)
    {
        var scale = (double)value;
        return double.IsFinite(scale)
            && scale >= MinimumRenderScale
            && scale <= MaximumRenderScale;
    }

    private static object CoerceIsBlurEnabled(DependencyObject dependencyObject, object baseValue)
    {
        if (dependencyObject is not BackdropBlurBorder border || baseValue is not true)
            return baseValue;

        return BackdropBlurCapabilities.ShouldEnableBlur(
            isBlurRequested: true,
            renderScale: border.RenderScale,
            isHighFidelitySupported: border.IsHighFidelityBlurSupported);
    }

    private static bool IsRenderingBiasValid(object value)
    {
        return value is RenderingBias.Performance or RenderingBias.Quality;
    }

    private static bool IsCornerRadiusValid(object value)
    {
        var radius = (CornerRadius)value;
        return IsNonNegativeFinite(radius.TopLeft)
            && IsNonNegativeFinite(radius.TopRight)
            && IsNonNegativeFinite(radius.BottomRight)
            && IsNonNegativeFinite(radius.BottomLeft);
    }

    private static bool IsNonNegativeFinite(double value)
    {
        return double.IsFinite(value) && value >= 0d;
    }

    private static double CalculateBlurOverscan(double blurRadius)
    {
        return Math.Ceiling(blurRadius * BlurOverscanFactor);
    }

    private readonly record struct BackdropGeometrySnapshot(
        FrameworkElement? Source,
        Rect Viewbox,
        Rect Viewport,
        bool IsActive,
        bool HasRecursiveSource)
    {
        internal static BackdropGeometrySnapshot Inactive(
            FrameworkElement source,
            bool hasRecursiveSource = false)
        {
            return new BackdropGeometrySnapshot(
                source,
                Rect.Empty,
                Rect.Empty,
                IsActive: false,
                HasRecursiveSource: hasRecursiveSource);
        }
    }

}
