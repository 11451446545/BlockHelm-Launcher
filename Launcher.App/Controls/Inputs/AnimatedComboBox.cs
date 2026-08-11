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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Animations;
using Launcher.App.Behaviors;

namespace Launcher.App.Controls;

/// <summary>
/// 在原生 ComboBox 弹出行为之上增加方向感知动画、滚轮转发和选择展示切换。
/// </summary>
public class AnimatedComboBox : ComboBox
{
    // 打开略慢、关闭略快；动画只修改透明度和变换，避免在 Popup 独立窗口中触发布局抖动。
    private const double PopupGap = 6;
    private const double PopupShadowPadding = 14;
    private const double DefaultDropDownItemHeightEstimate = 38;
    private const double PopupVerticalPaddingEstimate = 10;
    private const double PopupClosedScale = 0.97;
    private static readonly Duration OpenDuration = MotionDesign.StandardDuration;
    private static readonly Duration CloseDuration = MotionDesign.ShortDuration;
    private static readonly ConditionalWeakTable<Dispatcher, PopupWheelIsolationState> PopupWheelIsolationStates = new();

    public static readonly DependencyProperty IsPopupOpenProperty =
        DependencyProperty.Register(
            nameof(IsPopupOpen),
            typeof(bool),
            typeof(AnimatedComboBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDropDownClosingProperty =
        DependencyProperty.Register(
            nameof(IsDropDownClosing),
            typeof(bool),
            typeof(AnimatedComboBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty DropDownItemContainerStyleProperty =
        DependencyProperty.Register(
            nameof(DropDownItemContainerStyle),
            typeof(Style),
            typeof(AnimatedComboBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectionItemTemplateProperty =
        DependencyProperty.Register(
            nameof(SelectionItemTemplate),
            typeof(DataTemplate),
            typeof(AnimatedComboBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectionItemTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(SelectionItemTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(AnimatedComboBox),
            new PropertyMetadata(null));

    private readonly DependencyPropertyDescriptor dropDownDescriptor;
    private DispatcherTimer? closeTimer;
    private Popup? popup;
    private ListBox? popupListBox;
    private FrameworkElement? popupSurface;
    private FrameworkElement? popupTransformOwner;
    private TextBlock? selectionTextBlock;
    private ContentPresenter? selectionContentPresenter;
    private ScaleTransform? scaleTransform;
    private TranslateTransform? translateTransform;
    private InputManager? popupInputManager;
    private bool opensAbove;
    private bool isDropDownDescriptorAttached;
    private bool suppressMotionForKeyboardInput;
    private int keyboardInputGeneration;
    private long popupOpenGeneration;
    private long popupOpenAnimationStartedGeneration = -1;
    private PopupVisualState pendingOpenStartState;
    private bool pendingOpenAnimationEnabled;
    private bool pendingOpenMovementEnabled;

    static AnimatedComboBox()
    {
        // Popup 与宿主页面属于不同的路由树；在 ScrollViewer 类处理器处提供最后一道外层滚动隔离。
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(ScrollViewer_PreviewMouseWheelIsolation),
            handledEventsToo: true);
    }

    public AnimatedComboBox()
    {
        dropDownDescriptor = DependencyPropertyDescriptor.FromProperty(IsDropDownOpenProperty, typeof(ComboBox));
        AttachDropDownDescriptor();
        Loaded += AnimatedComboBox_Loaded;
        Unloaded += AnimatedComboBox_Unloaded;
    }

    public bool IsPopupOpen
    {
        get => (bool)GetValue(IsPopupOpenProperty);
        set => SetValue(IsPopupOpenProperty, value);
    }

    public bool IsDropDownClosing
    {
        get => (bool)GetValue(IsDropDownClosingProperty);
        set => SetValue(IsDropDownClosingProperty, value);
    }

    public Style? DropDownItemContainerStyle
    {
        get => (Style?)GetValue(DropDownItemContainerStyleProperty);
        set => SetValue(DropDownItemContainerStyleProperty, value);
    }

    public DataTemplate? SelectionItemTemplate
    {
        get => (DataTemplate?)GetValue(SelectionItemTemplateProperty);
        set => SetValue(SelectionItemTemplateProperty, value);
    }

    public DataTemplateSelector? SelectionItemTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(SelectionItemTemplateSelectorProperty);
        set => SetValue(SelectionItemTemplateSelectorProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Keyboard-driven toggles stay immediate; the suppression lasts through the routed key event.
        var generation = ++keyboardInputGeneration;
        suppressMotionForKeyboardInput = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (generation == keyboardInputGeneration)
                    suppressMotionForKeyboardInput = false;
            },
            DispatcherPriority.Input);

        base.OnPreviewKeyDown(e);
    }

    public override void OnApplyTemplate()
    {
        // 主题切换会重新应用模板，查找新部件前必须解除旧 Popup 和列表事件。
        DetachPopupListBox();
        DetachPopupSurface();
        DetachPopup();
        base.OnApplyTemplate();
        popup = GetTemplateChild("PART_Popup") as Popup;
        popupListBox = Template.FindName("PART_DropDownList", this) as ListBox;
        popupSurface = Template.FindName("PopupSurface", this) as FrameworkElement;
        selectionTextBlock = GetTemplateChild("SelectionTextBlock") as TextBlock;
        selectionContentPresenter = GetTemplateChild("SelectionContentPresenter") as ContentPresenter;
        AttachPopup();
        AttachPopupSurface();
        AttachPopupListBox();
        if (IsPopupOpen)
        {
            ActivatePopupWheelIsolation();
            AttachPopupInputGuard();
        }
        if (popupSurface is not null)
        {
            popupSurface.CacheMode = new BitmapCache();
            popupSurface.IsHitTestVisible = IsPopupOpen;
        }
        UpdateSelectionPresenterMode();
        EnsurePopupTransforms();
        if (IsPopupOpen && popupSurface is not null)
            RestorePopupOpenVisualState();
        else
            SetPopupVisualState(CreateClosedVisualState(MotionPreferences.ShouldAnimateMovement));
    }

    private void OnDropDownOpenChanged(object? sender, EventArgs e)
    {
        // IsDropDownOpen 是状态源；打开和关闭分别维护自己的计时器与命中状态。
        if (IsDropDownOpen)
        {
            BeginOpenAnimation();
            return;
        }

        BeginCloseAnimation();
    }

    private void BeginOpenAnimation()
    {
        // Popup 内部部件在控件首次应用模板时可能尚未加入名称域，因此打开前重新解析。
        var animateTransition = !suppressMotionForKeyboardInput;
        var animateMovement = animateTransition && MotionPreferences.ShouldAnimateMovement;
        closeTimer?.Stop();
        closeTimer = null;
        ResolvePopupContentParts();
        EnsurePopupTransforms();
        UpdatePopupPlacement();
        pendingOpenStartState = popupSurface is not null && IsPopupOpen
            ? CapturePopupVisualState()
            : CreateClosedVisualState(animateMovement);
        pendingOpenAnimationEnabled = animateTransition;
        pendingOpenMovementEnabled = animateMovement;
        IsDropDownClosing = false;
        if (popupSurface is not null)
        {
            StopPopupVisualAnimations();
            popupSurface.IsHitTestVisible = false;
            SetPopupVisualState(pendingOpenStartState);
        }

        var generation = ++popupOpenGeneration;
        ActivatePopupWheelIsolation();
        AttachPopupInputGuard();
        IsPopupOpen = true;
        if (!animateTransition)
            RestorePopupOpenVisualState();
        SchedulePopupOpenAnimation(generation);
    }

    private void BeginCloseAnimation()
    {
        // 关闭期间暂时保留 Popup 表面用于播放退场，计时结束后再回到完全关闭状态。
        if (!IsPopupOpen)
            return;

        popupOpenGeneration++;
        closeTimer?.Stop();
        closeTimer = null;
        IsDropDownClosing = true;
        var animateTransition = !suppressMotionForKeyboardInput;
        var animateMovement = animateTransition && MotionPreferences.ShouldAnimateMovement;
        var closedState = CreateClosedVisualState(animateMovement, useExitOffset: true);

        if (popupSurface is not null)
        {
            EnsurePopupTransforms();
            var currentState = CapturePopupVisualState();
            StopPopupVisualAnimations();
            popupSurface.IsHitTestVisible = false;
            SetPopupVisualState(closedState);

            if (animateTransition)
            {
                popupSurface.BeginAnimation(
                    OpacityProperty,
                    CreatePopupAnimation(
                        currentState.Opacity,
                        0,
                        MotionPreferences.ResolveOpacityDuration(CloseDuration)),
                    HandoffBehavior.SnapshotAndReplace);

                if (animateMovement)
                {
                    scaleTransform?.BeginAnimation(
                        ScaleTransform.ScaleXProperty,
                        CreatePopupAnimation(currentState.ScaleX, closedState.ScaleX, CloseDuration),
                        HandoffBehavior.SnapshotAndReplace);
                    scaleTransform?.BeginAnimation(
                        ScaleTransform.ScaleYProperty,
                        CreatePopupAnimation(currentState.ScaleY, closedState.ScaleY, CloseDuration),
                        HandoffBehavior.SnapshotAndReplace);
                    translateTransform?.BeginAnimation(
                        TranslateTransform.YProperty,
                        CreatePopupAnimation(currentState.TranslateY, closedState.TranslateY, CloseDuration),
                        HandoffBehavior.SnapshotAndReplace);
                }
            }
        }

        if (!animateTransition)
        {
            CompletePopupClose(closedState);
            return;
        }

        var closeInterval = MotionPreferences.ResolveOpacityDuration(CloseDuration).TimeSpan;
        if (animateMovement && CloseDuration.TimeSpan > closeInterval)
            closeInterval = CloseDuration.TimeSpan;

        closeTimer = new DispatcherTimer { Interval = closeInterval };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer?.Stop();
            closeTimer = null;
            CompletePopupClose(closedState);
        };
        closeTimer.Start();
    }

    private void EnsurePopupTransforms()
    {
        // 复用模板表面的 TransformGroup，不覆盖主题可能已经设置的其他变换。
        if (popupSurface is null)
            return;

        popupSurface.RenderTransformOrigin = opensAbove ? new Point(0.5, 1) : new Point(0.5, 0);
        if (ReferenceEquals(popupTransformOwner, popupSurface)
            && popupSurface.RenderTransform is TransformGroup existingGroup
            && scaleTransform is not null
            && translateTransform is not null
            && existingGroup.Children.Contains(scaleTransform)
            && existingGroup.Children.Contains(translateTransform))
        {
            return;
        }

        scaleTransform = new ScaleTransform(1, 1);
        translateTransform = new TranslateTransform(0, 0);
        var group = MotionDesign.EnsureTransformGroup(popupSurface);
        group.Children.Add(scaleTransform);
        group.Children.Add(translateTransform);
        popupTransformOwner = popupSurface;
    }

    private void SetPopupVisualState(PopupVisualState state)
    {
        if (popupSurface is null)
            return;

        popupSurface.Opacity = state.Opacity;
        if (scaleTransform is not null)
        {
            scaleTransform.ScaleX = state.ScaleX;
            scaleTransform.ScaleY = state.ScaleY;
        }
        if (translateTransform is not null)
            translateTransform.Y = state.TranslateY;
    }

    private void RestorePopupOpenVisualState()
    {
        if (popupSurface is null)
            return;

        popupSurface.CacheMode ??= new BitmapCache();
        EnsurePopupTransforms();
        StopPopupVisualAnimations();
        popupSurface.IsHitTestVisible = true;
        SetPopupVisualState(PopupVisualState.Open);
    }

    private void SchedulePopupOpenAnimation(long generation)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsPopupOpen || generation != popupOpenGeneration)
                return;

            ResolvePopupContentParts();
            if (popupSurface is not { IsLoaded: true })
                return;
            if (popupOpenAnimationStartedGeneration == generation)
                return;

            popupOpenAnimationStartedGeneration = generation;
            EnsurePopupTransforms();
            popupSurface.IsHitTestVisible = true;
            StopPopupVisualAnimations();

            if (!pendingOpenAnimationEnabled)
            {
                SetPopupVisualState(PopupVisualState.Open);
                return;
            }

            var startState = pendingOpenStartState;
            if (!pendingOpenMovementEnabled)
                startState = startState with { ScaleX = 1, ScaleY = 1, TranslateY = 0 };

            SetPopupVisualState(PopupVisualState.Open);
            popupSurface.BeginAnimation(
                OpacityProperty,
                CreatePopupAnimation(
                    startState.Opacity,
                    1,
                    MotionPreferences.ResolveOpacityDuration(OpenDuration)),
                HandoffBehavior.SnapshotAndReplace);

            if (pendingOpenMovementEnabled)
            {
                scaleTransform?.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    CreatePopupAnimation(startState.ScaleX, 1, OpenDuration),
                    HandoffBehavior.SnapshotAndReplace);
                scaleTransform?.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    CreatePopupAnimation(startState.ScaleY, 1, OpenDuration),
                    HandoffBehavior.SnapshotAndReplace);
                translateTransform?.BeginAnimation(
                    TranslateTransform.YProperty,
                    CreatePopupAnimation(startState.TranslateY, 0, OpenDuration),
                    HandoffBehavior.SnapshotAndReplace);
            }
        }, DispatcherPriority.Background);
    }

    private void CompletePopupClose(PopupVisualState closedState)
    {
        IsPopupOpen = false;
        DeactivatePopupWheelIsolation();
        DetachPopupInputGuard();
        IsDropDownClosing = false;
        if (popupSurface is null)
            return;

        StopPopupVisualAnimations();
        SetPopupVisualState(closedState);
    }

    private PopupVisualState CapturePopupVisualState()
    {
        return new PopupVisualState(
            popupSurface?.Opacity ?? 0,
            translateTransform?.Y ?? 0,
            scaleTransform?.ScaleX ?? 1,
            scaleTransform?.ScaleY ?? 1);
    }

    private PopupVisualState CreateClosedVisualState(bool animateMovement, bool useExitOffset = false)
    {
        if (!animateMovement)
            return new PopupVisualState(0, 0, 1, 1);

        return new PopupVisualState(
            0,
            useExitOffset ? GetCloseTranslateOffset() : GetOpenTranslateOffset(),
            PopupClosedScale,
            PopupClosedScale);
    }

    private void StopPopupVisualAnimations()
    {
        popupSurface?.BeginAnimation(OpacityProperty, null);
        scaleTransform?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translateTransform?.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private static DoubleAnimation CreatePopupAnimation(double from, double to, Duration duration)
    {
        return new DoubleAnimation(from, to, duration)
        {
            EasingFunction = MotionDesign.StrongEaseOut,
            FillBehavior = FillBehavior.Stop
        };
    }

    private void ResolvePopupContentParts()
    {
        if (popupSurface is null)
        {
            popupSurface = Template.FindName("PopupSurface", this) as FrameworkElement;
            AttachPopupSurface();
        }

        if (popupListBox is null)
        {
            popupListBox = Template.FindName("PART_DropDownList", this) as ListBox;
            AttachPopupListBox();
        }
    }

    private void UpdatePopupPlacement()
    {
        // WPF 会按屏幕空间自动翻转 Popup，因此根据屏幕坐标判断真实展开方向。
        if (popup is null)
            return;

        var popupHeight = GetPopupHeightEstimate() + PopupShadowPadding * 2;
        var topLeft = PointToScreen(new Point(0, 0));
        var controlTop = topLeft.Y;
        var controlBottom = topLeft.Y + ActualHeight;
        var belowSpace = SystemParameters.WorkArea.Bottom - controlBottom;
        var aboveSpace = controlTop - SystemParameters.WorkArea.Top;

        opensAbove = belowSpace < popupHeight + PopupGap && aboveSpace > belowSpace;

        popup.Placement = opensAbove ? PlacementMode.Top : PlacementMode.Bottom;
        popup.HorizontalOffset = 0;
        popup.VerticalOffset = opensAbove
            ? PopupShadowPadding - PopupGap
            : PopupGap - PopupShadowPadding;
        EnsurePopupTransforms();
    }

    private double GetPopupHeightEstimate()
    {
        // 首次展开前 ActualHeight 为零，使用条目高度和 MaxDropDownHeight 给动画原点一个稳定估值。
        var maxHeight = double.IsNaN(MaxDropDownHeight) || MaxDropDownHeight <= 0 ? 260 : MaxDropDownHeight;
        if (Items.Count <= 0)
            return maxHeight;

        var desiredHeight = Items.Count * GetDropDownItemHeightEstimate() + PopupVerticalPaddingEstimate;
        return Math.Min(desiredHeight, maxHeight);
    }

    private double GetOpenTranslateOffset() => opensAbove ? 10 : -10;

    private double GetCloseTranslateOffset() => opensAbove ? 8 : -8;

    private double GetDropDownItemHeightEstimate()
    {
        if (ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement container
            && container.ActualHeight > 0)
        {
            return container.ActualHeight;
        }

        return Math.Max(DefaultDropDownItemHeightEstimate, FontSize + 22);
    }

    private void AttachPopupListBox()
    {
        // Popup 内容可能延迟生成，附加方法保持幂等，允许在模板应用和 Opened 时重复调用。
        if (popupListBox is null)
            return;

        popupListBox.SelectionChanged += PopupListBox_SelectionChanged;
        popupListBox.PreviewMouseLeftButtonUp += PopupListBox_PreviewMouseLeftButtonUp;
        popupListBox.PreviewKeyDown += PopupListBox_PreviewKeyDown;
        popupListBox.PreviewMouseWheel += PopupDropDown_PreviewMouseWheel;
    }

    private void AttachPopupSurface()
    {
        if (popupSurface is null)
            return;

        popupSurface.Loaded += PopupSurface_Loaded;
        popupSurface.PreviewMouseWheel += PopupDropDown_PreviewMouseWheel;
    }

    private void DetachPopupListBox()
    {
        if (popupListBox is null)
            return;

        popupListBox.SelectionChanged -= PopupListBox_SelectionChanged;
        popupListBox.PreviewMouseLeftButtonUp -= PopupListBox_PreviewMouseLeftButtonUp;
        popupListBox.PreviewKeyDown -= PopupListBox_PreviewKeyDown;
        popupListBox.PreviewMouseWheel -= PopupDropDown_PreviewMouseWheel;
        popupListBox = null;
    }

    private void PopupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 模板卸载或 ItemsSource 清理只会移除选择；这种瞬时 null 不能反向清空业务选择。
        var selectedItem = e.AddedItems.Cast<object?>().FirstOrDefault(item => item is not null);
        if (selectedItem is null)
            return;

        SetCurrentValue(SelectedItemProperty, selectedItem);
    }

    private void AnimatedComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        AttachDropDownDescriptor();
    }

    private void AnimatedComboBox_Unloaded(object sender, RoutedEventArgs e)
    {
        closeTimer?.Stop();
        DetachPopupListBox();
        DetachPopupSurface();
        DeactivatePopupWheelIsolation();
        DetachPopupInputGuard();
        DetachPopup();
        DetachDropDownDescriptor();
    }

    private void AttachDropDownDescriptor()
    {
        if (isDropDownDescriptorAttached)
            return;

        dropDownDescriptor.AddValueChanged(this, OnDropDownOpenChanged);
        isDropDownDescriptorAttached = true;
    }

    private void DetachDropDownDescriptor()
    {
        if (!isDropDownDescriptorAttached)
            return;

        dropDownDescriptor.RemoveValueChanged(this, OnDropDownOpenChanged);
        isDropDownDescriptorAttached = false;
    }

    private void DetachPopupSurface()
    {
        if (popupSurface is null)
            return;

        popupSurface.Loaded -= PopupSurface_Loaded;
        popupSurface.PreviewMouseWheel -= PopupDropDown_PreviewMouseWheel;
        popupSurface = null;
    }

    private void PopupSurface_Loaded(object sender, RoutedEventArgs e)
    {
        if (IsPopupOpen)
            SchedulePopupOpenAnimation(popupOpenGeneration);
    }

    private void AttachPopup()
    {
        if (popup is null)
            return;

        popup.Opened += Popup_Opened;
        popup.Closed += Popup_Closed;
    }

    private void DetachPopup()
    {
        if (popup is null)
            return;

        popup.Opened -= Popup_Opened;
        popup.Closed -= Popup_Closed;
        DetachPopupInputGuard();
        popup = null;
    }

    private void Popup_Opened(object? sender, EventArgs e)
    {
        ResolvePopupContentParts();
        AttachPopupInputGuard();
        popupListBox?.Focus();
    }

    private void Popup_Closed(object? sender, EventArgs e)
    {
        StopPopupScrollAnimation();
        DeactivatePopupWheelIsolation();
        DetachPopupInputGuard();
    }

    private void PopupListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 在预览阶段开始关闭，避免默认选择行为先销毁 Popup 导致退场动画丢失。
        if (!IsDropDownOpen || sender is not ListBox listBox || e.OriginalSource is not DependencyObject source)
            return;

        if (ItemsControl.ContainerFromElement(listBox, source) is ListBoxItem { IsEnabled: true })
            IsDropDownOpen = false;
    }

    private void PopupListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsDropDownOpen)
            return;

        if (e.Key is Key.Enter or Key.Space or Key.Escape)
        {
            suppressMotionForKeyboardInput = true;
            try
            {
                IsDropDownOpen = false;
            }
            finally
            {
                suppressMotionForKeyboardInput = false;
            }

            e.Handled = true;
        }
    }

    private void PopupDropDown_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Popup 是独立窗口，滚轮事件不会自然路由到宿主控件，需要显式交给内部列表滚动。
        ProcessOpenPopupMouseWheel(e, cursorOverPopup: true);
    }

    private void InputManager_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        // PreviewMouseWheel 尚未进入任一 PresentationSource 的路由树，在此消费可同时覆盖 Popup 与宿主窗口。
        if (e.StagingItem.Input is not MouseWheelEventArgs mouseWheel
            || mouseWheel.RoutedEvent != Mouse.PreviewMouseWheelEvent)
        {
            return;
        }

        ProcessOpenPopupMouseWheel(mouseWheel, IsCursorOverPopupSurface());
    }

    internal bool ProcessOpenPopupMouseWheel(MouseWheelEventArgs e, bool cursorOverPopup)
    {
        if (!IsPopupOpen)
            return false;

        // 先标记已处理，确保列表尚未生成或没有可滚动范围时也不会把事件泄漏给外层页面。
        e.Handled = true;
        if (cursorOverPopup)
            ScrollPopupList(e);
        return true;
    }

    private void ScrollPopupList(MouseWheelEventArgs e)
    {
        if (popupListBox is not { } listBox)
            return;

        listBox.ApplyTemplate();
        listBox.UpdateLayout();
        SmoothScrollBehavior.HandleMouseWheelFromDescendant(listBox, e, handleWhenUnavailable: true);
    }

    internal void AttachPopupInputGuard()
    {
        var inputManager = InputManager.Current;
        if (ReferenceEquals(popupInputManager, inputManager))
            return;

        DetachPopupInputGuard();
        popupInputManager = inputManager;
        popupInputManager.PreProcessInput += InputManager_PreProcessInput;
    }

    internal void DetachPopupInputGuard()
    {
        if (popupInputManager is null)
            return;

        popupInputManager.PreProcessInput -= InputManager_PreProcessInput;
        popupInputManager = null;
    }

    internal void ActivatePopupWheelIsolation()
    {
        var state = PopupWheelIsolationStates.GetOrCreateValue(Dispatcher);
        state.ActiveComboBox = new WeakReference<AnimatedComboBox>(this);
    }

    internal void DeactivatePopupWheelIsolation()
    {
        if (!PopupWheelIsolationStates.TryGetValue(Dispatcher, out var state)
            || state.ActiveComboBox is not { } activeReference
            || !activeReference.TryGetTarget(out var activeComboBox)
            || !ReferenceEquals(activeComboBox, this))
        {
            return;
        }

        state.ActiveComboBox = null;
    }

    private static void ScrollViewer_PreviewMouseWheelIsolation(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || !PopupWheelIsolationStates.TryGetValue(scrollViewer.Dispatcher, out var state)
            || state.ActiveComboBox is not { } activeReference
            || !activeReference.TryGetTarget(out var activeComboBox))
        {
            return;
        }

        if (!activeComboBox.IsPopupOpen)
        {
            state.ActiveComboBox = null;
            return;
        }

        if (!activeComboBox.IsPopupScrollViewer(scrollViewer))
            e.Handled = true;
    }

    private bool IsPopupScrollViewer(DependencyObject scrollViewer)
    {
        if (popupListBox is null)
            return false;

        for (DependencyObject? current = scrollViewer; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, popupListBox))
                return true;
        }

        return false;
    }

    private bool IsCursorOverPopupSurface()
    {
        if (popupSurface is null || !GetCursorPos(out var cursor))
            return false;

        var point = popupSurface.PointFromScreen(new Point(cursor.X, cursor.Y));
        return point.X >= 0
            && point.Y >= 0
            && point.X <= popupSurface.ActualWidth
            && point.Y <= popupSurface.ActualHeight;
    }

    private void UpdateSelectionPresenterMode()
    {
        // 打开列表时隐藏模板中的重复选择展示，关闭后再恢复紧凑的单项展示。
        if (selectionTextBlock is null || selectionContentPresenter is null)
            return;

        var useSelectionTemplate = SelectionItemTemplate is not null || SelectionItemTemplateSelector is not null;
        selectionTextBlock.Visibility = useSelectionTemplate ? Visibility.Collapsed : Visibility.Visible;
        selectionContentPresenter.Visibility = useSelectionTemplate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StopPopupScrollAnimation()
    {
        // Popup 关闭或模板替换时停止旧 ScrollViewer 动画，避免动画时钟继续持有视觉对象。
        if (popupListBox is null)
            return;

        popupListBox.ApplyTemplate();
        popupListBox.UpdateLayout();
        SmoothScrollBehavior.CancelAnimationFromDescendant(popupListBox);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    private readonly record struct PopupVisualState(
        double Opacity,
        double TranslateY,
        double ScaleX,
        double ScaleY)
    {
        public static PopupVisualState Open { get; } = new(1, 0, 1, 1);
    }

    private sealed class PopupWheelIsolationState
    {
        public WeakReference<AnimatedComboBox>? ActiveComboBox { get; set; }
    }
}
