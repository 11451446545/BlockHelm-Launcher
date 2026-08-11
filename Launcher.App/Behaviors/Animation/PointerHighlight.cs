/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.App.Animations;

namespace Launcher.App.Behaviors;

/// <summary>
/// Provides a pointer-originated highlight for every <see cref="ButtonBase"/> without
/// requiring control templates to own another visual layer.
/// </summary>
public static class PointerHighlight
{
    private static PointerHighlightState? currentHoverState;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PointerHighlight),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(PointerHighlightState),
            typeof(PointerHighlight),
            new PropertyMetadata(null));

    static PointerHighlight()
    {
        EventManager.RegisterClassHandler(
            typeof(ButtonBase),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ButtonBase_Loaded));
    }

    public static void EnsureRegistered()
    {
    }

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void ButtonBase_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button || !ShouldAutoEnable(button.GetType()))
            return;

        var valueSource = DependencyPropertyHelper.GetValueSource(button, IsEnabledProperty);
        if (valueSource.BaseValueSource == BaseValueSource.Default && !GetIsEnabled(button))
            button.SetCurrentValue(IsEnabledProperty, true);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ButtonBase button)
            return;

        if (e.NewValue is true)
        {
            button.MouseEnter += Button_MouseEnter;
            button.MouseMove += Button_MouseMove;
            button.MouseLeave += Button_MouseLeave;
            button.PreviewMouseDown += Button_PreviewMouseDown;
            button.PreviewMouseUp += Button_PreviewMouseUp;
            button.LostMouseCapture += Button_LostMouseCapture;
            button.IsEnabledChanged += Button_IsEnabledChanged;
            button.Unloaded += Button_Unloaded;
            return;
        }

        button.MouseEnter -= Button_MouseEnter;
        button.MouseMove -= Button_MouseMove;
        button.MouseLeave -= Button_MouseLeave;
        button.PreviewMouseDown -= Button_PreviewMouseDown;
        button.PreviewMouseUp -= Button_PreviewMouseUp;
        button.LostMouseCapture -= Button_LostMouseCapture;
        button.IsEnabledChanged -= Button_IsEnabledChanged;
        button.Unloaded -= Button_Unloaded;
        RemoveState(button);
    }

    private static void Button_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not ButtonBase button || !ShouldActivate(button.IsEnabled, IsRealMouse(e)))
            return;

        var state = GetOrCreateState(button);
        if (currentHoverState is not null && !ReferenceEquals(currentHoverState, state))
            currentHoverState.HideImmediately();

        currentHoverState = state;
        state.Show(e.GetPosition(button));
    }

    private static void Button_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is ButtonBase button
            && ShouldActivate(button.IsEnabled, IsRealMouse(e))
            && button.GetValue(StateProperty) is PointerHighlightState state)
        {
            state.Move(e.GetPosition(button));
        }
    }

    private static void Button_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not ButtonBase button
            || button.GetValue(StateProperty) is not PointerHighlightState state)
        {
            return;
        }

        if (ReferenceEquals(currentHoverState, state))
            currentHoverState = null;
        state.Hide();
    }

    private static void Button_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left
            && sender is ButtonBase button
            && ShouldActivate(button.IsEnabled, IsRealMouse(e))
            && button.GetValue(StateProperty) is PointerHighlightState state)
        {
            state.SetPressed(true);
        }
    }

    private static void Button_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left
            && sender is ButtonBase button
            && button.GetValue(StateProperty) is PointerHighlightState state)
        {
            state.SetPressed(false);
        }
    }

    private static void Button_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is ButtonBase button
            && button.GetValue(StateProperty) is PointerHighlightState state)
        {
            state.SetPressed(false);
        }
    }

    private static void Button_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && sender is ButtonBase button)
            RemoveState(button);
    }

    private static void Button_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ButtonBase button)
            RemoveState(button);
    }

    private static PointerHighlightState GetOrCreateState(ButtonBase button)
    {
        if (button.GetValue(StateProperty) is PointerHighlightState state)
            return state;

        state = new PointerHighlightState(button);
        button.SetValue(StateProperty, state);
        return state;
    }

    private static void RemoveState(ButtonBase button)
    {
        if (button.GetValue(StateProperty) is not PointerHighlightState state)
            return;

        if (ReferenceEquals(currentHoverState, state))
            currentHoverState = null;
        state.Dispose();
        button.ClearValue(StateProperty);
    }

    internal static PointerHighlightSnapshot ResolveSnapshot(
        Size size,
        Point pointerPosition,
        bool isPressed,
        bool reducedMotion) =>
        ResolveSnapshot(size, pointerPosition, isPressed ? 1d : 0d, reducedMotion);

    internal static PointerHighlightSnapshot ResolveSnapshot(
        Size size,
        Point pointerPosition,
        double pressProgress,
        bool reducedMotion)
    {
        var center = reducedMotion
            ? new Point(size.Width / 2d, size.Height / 2d)
            : new Point(
                Math.Clamp(pointerPosition.X, 0d, Math.Max(0d, size.Width)),
                Math.Clamp(pointerPosition.Y, 0d, Math.Max(0d, size.Height)));
        var normalizedPressProgress = Math.Clamp(pressProgress, 0d, 1d);
        return new PointerHighlightSnapshot(
            center,
            68d - (26d * normalizedPressProgress),
            0.22d + (0.12d * normalizedPressProgress));
    }

    internal static bool ShouldActivate(bool isEnabled, bool isPointerInteraction) =>
        isEnabled && isPointerInteraction;

    internal static bool ShouldAutoEnable(Type buttonType) =>
        !typeof(RepeatButton).IsAssignableFrom(buttonType);

    internal readonly record struct PointerHighlightSnapshot(Point Center, double Radius, double Intensity);

    private sealed class PointerHighlightState(ButtonBase button) : IDisposable
    {
        private readonly ButtonBase button = button;
        private AdornerLayer? layer;
        private PointerHighlightAdorner? adorner;
        private int fadeGeneration;

        public void Show(Point pointerPosition)
        {
            if (!button.IsEnabled)
                return;

            EnsureAdorner();
            if (adorner is null)
                return;

            fadeGeneration++;
            adorner.BeginAnimation(UIElement.OpacityProperty, null);
            adorner.Opacity = 1d;
            adorner.Update(pointerPosition);
            adorner.SetPressed(false);
        }

        public void Move(Point pointerPosition)
        {
            if (!MotionPreferences.ShouldAnimateMovement)
                return;

            adorner?.Update(pointerPosition);
        }

        public void SetPressed(bool isPressed)
        {
            adorner?.SetPressed(isPressed);
        }

        public void Hide()
        {
            if (adorner is null)
                return;

            adorner.SetPressed(false);
            if (MotionPreferences.IsReducedMotionEnabled)
            {
                HideImmediately();
                return;
            }

            var generation = ++fadeGeneration;
            var animation = new DoubleAnimation(adorner.Opacity, 0d, MotionDesign.FastDuration)
            {
                EasingFunction = MotionDesign.StrongEaseOut,
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                if (generation == fadeGeneration)
                    HideImmediately();
            };
            adorner.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        public void HideImmediately()
        {
            fadeGeneration++;
            if (adorner is null)
                return;

            adorner.BeginAnimation(UIElement.OpacityProperty, null);
            layer?.Remove(adorner);
            adorner = null;
            layer = null;
        }

        public void Dispose() => HideImmediately();

        private void EnsureAdorner()
        {
            if (adorner is not null)
                return;

            layer = AdornerLayer.GetAdornerLayer(button);
            if (layer is null)
                return;

            adorner = new PointerHighlightAdorner(button);
            layer.Add(adorner);
        }
    }

    private sealed class PointerHighlightAdorner : Adorner
    {
        private static readonly Duration PressInDuration = TimeSpan.FromMilliseconds(140);
        private static readonly Duration ReleaseDuration = TimeSpan.FromMilliseconds(100);
        private static readonly Brush NormalHighlightBrush = CreateHighlightBrush(0.22d);
        private static readonly Brush PressedHighlightBrush = CreateHighlightBrush(0.34d);

        private Point pointerPosition;
        private readonly RectangleGeometry clipGeometry = new();
        private readonly double clipRadius;
        private bool hasPointerPosition;
        private int pressGeneration;

        public static readonly DependencyProperty PressProgressProperty =
            DependencyProperty.Register(
                nameof(PressProgress),
                typeof(double),
                typeof(PointerHighlightAdorner),
                new FrameworkPropertyMetadata(
                    0d,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    null,
                    CoercePressProgress));

        public PointerHighlightAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            clipRadius = ResolveClipRadius(adornedElement);
        }

        public bool IsPressed { get; private set; }

        public double PressProgress
        {
            get => (double)GetValue(PressProgressProperty);
            private set => SetValue(PressProgressProperty, value);
        }

        public void Update(Point nextPointerPosition)
        {
            if (hasPointerPosition
                && (nextPointerPosition - pointerPosition).LengthSquared < 0.0625d)
            {
                return;
            }

            pointerPosition = nextPointerPosition;
            hasPointerPosition = true;
            InvalidateVisual();
        }

        public void SetPressed(bool isPressed)
        {
            if (IsPressed == isPressed)
                return;

            IsPressed = isPressed;
            var target = isPressed ? 1d : 0d;
            var current = PressProgress;
            BeginAnimation(PressProgressProperty, null);
            PressProgress = current;

            var generation = ++pressGeneration;
            if (MotionPreferences.IsReducedMotionEnabled)
            {
                PressProgress = target;
                return;
            }

            var duration = isPressed ? PressInDuration : ReleaseDuration;
            var animation = new DoubleAnimation(current, target, duration)
            {
                EasingFunction = MotionDesign.StrongEaseOut,
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                if (generation == pressGeneration)
                    PressProgress = target;
            };
            BeginAnimation(PressProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var snapshot = ResolveSnapshot(
                RenderSize,
                pointerPosition,
                PressProgress,
                MotionPreferences.IsReducedMotionEnabled);
            if (snapshot.Radius <= 0d || snapshot.Intensity <= 0d)
                return;

            clipGeometry.Rect = new Rect(RenderSize);
            clipGeometry.RadiusX = Math.Min(clipRadius, RenderSize.Width / 2d);
            clipGeometry.RadiusY = Math.Min(clipRadius, RenderSize.Height / 2d);
            drawingContext.PushClip(clipGeometry);
            var pressedOpacity = Math.Clamp(PressProgress, 0d, 1d);
            if (pressedOpacity < 1d)
            {
                drawingContext.PushOpacity(1d - pressedOpacity);
                drawingContext.DrawEllipse(NormalHighlightBrush, null, snapshot.Center, snapshot.Radius, snapshot.Radius);
                drawingContext.Pop();
            }

            if (pressedOpacity > 0d)
            {
                drawingContext.PushOpacity(pressedOpacity);
                drawingContext.DrawEllipse(PressedHighlightBrush, null, snapshot.Center, snapshot.Radius, snapshot.Radius);
                drawingContext.Pop();
            }

            drawingContext.Pop();
        }

        private static object CoercePressProgress(DependencyObject _, object value) =>
            Math.Clamp((double)value, 0d, 1d);

        private static Brush CreateHighlightBrush(double intensity)
        {
            var brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5d, 0.5d),
                Center = new Point(0.5d, 0.5d),
                RadiusX = 0.5d,
                RadiusY = 0.5d
            };
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)Math.Round(intensity * 255d), 255, 255, 255),
                0d));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1d));
            brush.Freeze();
            return brush;
        }

        private static double ResolveClipRadius(UIElement adornedElement)
        {
            if (adornedElement is not FrameworkElement root
                || root.ActualWidth <= 0d
                || root.ActualHeight <= 0d)
            {
                return 0d;
            }

            var minimumWidth = root.ActualWidth * 0.9d;
            var minimumHeight = root.ActualHeight * 0.9d;
            var bestRadius = 0d;
            Visit(root);
            return bestRadius;

            void Visit(DependencyObject current)
            {
                var childCount = VisualTreeHelper.GetChildrenCount(current);
                for (var index = 0; index < childCount; index++)
                {
                    var child = VisualTreeHelper.GetChild(current, index);
                    if (child is Border border
                        && border.ActualWidth >= minimumWidth
                        && border.ActualHeight >= minimumHeight)
                    {
                        var radius = border.CornerRadius;
                        bestRadius = Math.Max(
                            bestRadius,
                            Math.Max(
                                Math.Max(radius.TopLeft, radius.TopRight),
                                Math.Max(radius.BottomRight, radius.BottomLeft)));
                    }

                    Visit(child);
                }
            }
        }
    }

    private static bool IsRealMouse(MouseEventArgs e) => e.StylusDevice is null;
}
