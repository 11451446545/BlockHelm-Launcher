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
        UpdateDialogKindText = update.ReleaseKind == LauncherReleaseKind.Patch ? Strings.Dialog_UpdateKindPatch : Strings.Dialog_UpdateKindFull;
        UpdateDialogInstallHint = !update.IsApplicable ? Strings.Dialog_UpdatePatchNotApplicable
            : !update.CanAutoInstall ? Strings.Dialog_UpdateInstallerHint : string.Empty;
        OnPropertyChanged(nameof(ConfirmUpdateButtonText));
        UpdateDialogVersionText = update.DisplayVersion;
        UpdateDialogMessage = string.Format(Strings.Dialog_UpdateAvailableVersionFormat, update.DisplayVersion);
        UpdateDialogChangelog = string.IsNullOrWhiteSpace(update.Changelog)
            ? Strings.Dialog_UpdateChangelogUnavailable : update.Changelog.Trim();
        // Older schema-1 manifests carry only releaseNotes; keep them readable without a server migration.
        var summary = !string.IsNullOrWhiteSpace(update.Summary) ? update.Summary.Trim()
            : update.Changelog?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().TrimStart('#').Trim()).FirstOrDefault(line => line.Length > 0);
        UpdateDialogSummary = string.IsNullOrWhiteSpace(summary) ? Strings.Dialog_UpdateSummaryUnavailable
            : summary.Length <= 800 ? summary : summary[..800] + "…";
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

    private LauncherReleaseIdentity ResolveReleaseIdentity()
    {
        var metadata = typeof(InfoSettingsViewModel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(value => value.Key, value => value.Value);
        metadata.TryGetValue("ReleaseId", out var id);
        metadata.TryGetValue("ReleaseSequence", out var sequence);
        return new(LauncherVersionText, id ?? LauncherVersionText,
            long.TryParse(sequence, out var order) ? order : 0);
    }

    private enum UpdateCheckPresentation
    {
        Manual,
        StartupSilent
    }
}
