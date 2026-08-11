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

namespace Launcher.App.Services;

public sealed class NavigationMenuAnimationService
{
    private const double CollapsedWidth = 62;
    private const double ExpandedWidth = 176;

    private readonly ColumnDefinition menuColumn;

    public NavigationMenuAnimationService(ColumnDefinition menuColumn)
    {
        this.menuColumn = menuColumn;
    }

    public void SetExpanded(bool isExpanded)
    {
        menuColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        menuColumn.Width = GetWidth(isExpanded);
    }

    public void AnimateExpanded(bool isExpanded)
    {
        // Column width participates in the entire shell's measure/arrange pass. Commit that layout
        // immediately; NavigationMenuTextStyle supplies the short opacity feedback for the new state.
        SetExpanded(isExpanded);
    }

    private static GridLength GetWidth(bool isExpanded)
    {
        return new GridLength(isExpanded ? ExpandedWidth : CollapsedWidth);
    }
}
