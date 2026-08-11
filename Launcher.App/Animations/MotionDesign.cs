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
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Launcher.App.Animations;

public static class MotionDesign
{
    public static readonly Duration FastDuration = TimeSpan.FromMilliseconds(100);
    public static readonly Duration ShortDuration = TimeSpan.FromMilliseconds(160);
    public static readonly Duration StandardDuration = TimeSpan.FromMilliseconds(220);
    public static readonly Duration EmphasizedDuration = TimeSpan.FromMilliseconds(260);

    public static readonly IEasingFunction StrongEaseOut = CreateFrozenEasing(
        new Point(0.23, 1),
        new Point(0.32, 1));

    public static readonly IEasingFunction StrongEaseInOut = CreateFrozenEasing(
        new Point(0.77, 0),
        new Point(0.175, 1));

    public static TransformGroup EnsureTransformGroup(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.RenderTransform is TransformGroup existingGroup)
        {
            if (!existingGroup.IsFrozen)
                return existingGroup;

            var clonedGroup = existingGroup.CloneCurrentValue();
            element.SetCurrentValue(UIElement.RenderTransformProperty, clonedGroup);
            return clonedGroup;
        }

        var group = new TransformGroup();
        var existingTransform = element.RenderTransform;
        if (existingTransform is not null && existingTransform != Transform.Identity)
            group.Children.Add(existingTransform);

        element.SetCurrentValue(UIElement.RenderTransformProperty, group);
        return group;
    }

    private static IEasingFunction CreateFrozenEasing(Point controlPoint1, Point controlPoint2)
    {
        var easing = new KeySplineEasingFunction(controlPoint1, controlPoint2);
        easing.Freeze();
        return easing;
    }

    private sealed class KeySplineEasingFunction : Freezable, IEasingFunction
    {
        private readonly Point controlPoint1;
        private readonly Point controlPoint2;
        private readonly KeySpline keySpline;

        public KeySplineEasingFunction(Point controlPoint1, Point controlPoint2)
        {
            this.controlPoint1 = controlPoint1;
            this.controlPoint2 = controlPoint2;
            keySpline = new KeySpline(controlPoint1, controlPoint2);
        }

        public double Ease(double normalizedTime)
        {
            return keySpline.GetSplineProgress(Math.Clamp(normalizedTime, 0, 1));
        }

        protected override Freezable CreateInstanceCore()
        {
            return new KeySplineEasingFunction(controlPoint1, controlPoint2);
        }
    }
}
