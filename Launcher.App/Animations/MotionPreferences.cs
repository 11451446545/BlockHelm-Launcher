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
using System.Windows.Media.Animation;

namespace Launcher.App.Animations;

public static class MotionPreferences
{
    public static bool IsReducedMotionEnabled => !SystemParameters.ClientAreaAnimation;

    public static bool ShouldAnimateMovement => !IsReducedMotionEnabled;

    public static Duration ResolveMovementDuration(Duration preferredDuration)
    {
        return ShouldAnimateMovement ? preferredDuration : TimeSpan.Zero;
    }

    public static Duration ResolveOpacityDuration(Duration preferredDuration)
    {
        return IsReducedMotionEnabled ? MotionDesign.FastDuration : preferredDuration;
    }
}
