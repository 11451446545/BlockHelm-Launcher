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

using System.Reflection;
using Launcher.App.Resources;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class InfoSettingsViewModel
{
    private void ShowUpdateAvailableDialog(LauncherUpdateInfo update)
    {
        availableUpdate = update;
        UpdateDialogVersionText = update.DisplayVersion;
        UpdateDialogMessage = string.Format(Strings.Dialog_UpdateAvailableVersionFormat, update.DisplayVersion);
        updateDialogReleasePageUrl = update.ReleasePageUrl;
        IsUpdateAvailableDialogOpen = true;
        ConfirmUpdateCommand.NotifyCanExecuteChanged();
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

    private void ReportVisibleStatus(string message)
    {
        statusService.Report(message);
        floatingMessageService.Show(message);
    }

    private bool TryOpenUpdateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            return externalLinkService.TryOpen(url);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ResolveLauncherVersion()
    {
        var assembly = typeof(InfoSettingsViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Trim();

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion)
            ? Strings.Settings_LauncherVersionUnknown
            : assemblyVersion;
    }

    private enum UpdateCheckPresentation
    {
        Manual,
        StartupSilent
    }
}
