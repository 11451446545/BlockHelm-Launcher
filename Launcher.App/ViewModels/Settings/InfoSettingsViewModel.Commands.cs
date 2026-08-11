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

using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.Application;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class InfoSettingsViewModel
{
    [RelayCommand]
    private void OpenGithubRepository()
    {
        try
        {
            if (!externalLinkService.TryOpen(LauncherProjectLinks.GitHubRepositoryUrl))
                statusService.Report(Strings.Status_OpenGithubRepositoryFailed);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_OpenGithubRepositoryFailed);
        }
    }

    [RelayCommand]
    private void OpenReferenceProject(InfoReferenceProjectItem? project)
    {
        if (project is null)
            return;

        try
        {
            if (!externalLinkService.TryOpen(project.ProjectUrl))
                ReportVisibleStatus(Strings.Status_OpenReferenceProjectFailed);
        }
        catch (Exception)
        {
            ReportVisibleStatus(Strings.Status_OpenReferenceProjectFailed);
        }
    }

    [RelayCommand]
    private void OpenCopyrightNotice()
    {
        OpenLegalDocument(LauncherProjectLinks.GitHubRepositoryUrl);
    }

    [RelayCommand]
    private void OpenOpenSourceLicense()
    {
        OpenLegalDocument(LauncherProjectLinks.GitHubLicenseUrl);
    }

    [RelayCommand]
    private void OpenUserAgreement()
    {
        OpenLegalDocument(LauncherProjectLinks.UserAgreementUrl);
    }

    private void OpenLegalDocument(string url)
    {
        try
        {
            if (externalLinkService.TryOpen(url))
                return;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open a legal document link.");
            ReportVisibleStatus(Strings.Status_OpenLegalDocumentFailed);
            return;
        }

        logger.LogWarning("Failed to open a legal document link.");
        ReportVisibleStatus(Strings.Status_OpenLegalDocumentFailed);
    }

    [RelayCommand(CanExecute = nameof(CanCheckUpdates), AllowConcurrentExecutions = true)]
    private async Task CheckUpdatesAsync()
    {
        await CheckUpdatesCoreAsync(UpdateCheckPresentation.Manual);
    }

    public Task CheckUpdatesOnStartupAsync()
    {
        return CheckUpdatesCoreAsync(UpdateCheckPresentation.StartupSilent);
    }

    [RelayCommand]
    private void OpenUpdateChangelog()
    {
        if (!TryOpenUpdateUrl(updateDialogReleasePageUrl))
            ReportVisibleStatus(Strings.Status_OpenUpdatePageFailed);
    }

    [RelayCommand]
    private void CancelUpdateDialog()
    {
        IsUpdateAvailableDialogOpen = false;
        availableUpdate = null;
        ConfirmUpdateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConfirmUpdate))]
    private async Task ConfirmUpdateAsync()
    {
        if (IsStartingUpdate)
            return;

        var update = availableUpdate;
        if (update is null)
        {
            ReportVisibleStatus(Strings.Status_OpenUpdatePageFailed);
            return;
        }

        if (!update.CanAutoInstall)
        {
            ReportVisibleStatus(Strings.Status_UpdateAutoInstallPackageNotFound);
            return;
        }

        IsStartingUpdate = true;
        ReportStatus(Strings.Status_DownloadingLauncherUpdate);
        try
        {
            var result = await launcherSelfUpdateService.StartUpdateAsync(update);
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "Launcher self-update could not be started. Version={Version} Reason={Reason}",
                    update.Version,
                    result.ErrorMessage ?? "unknown");
                ReportVisibleStatus(Strings.Status_LauncherUpdateStartFailed);
                return;
            }

            IsUpdateAvailableDialogOpen = false;
            availableUpdate = null;
            ConfirmUpdateCommand.NotifyCanExecuteChanged();
            ReportStatus(Strings.Status_LauncherUpdateRestarting);
            applicationExitService.Shutdown();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Launcher self-update threw an exception.");
            ReportVisibleStatus(Strings.Status_LauncherUpdateStartFailed);
        }
        finally
        {
            IsStartingUpdate = false;
        }
    }

    private bool CanCheckUpdates()
    {
        return !IsStartingUpdate && !isUpdateCheckRunning;
    }

    private bool CanConfirmUpdate()
    {
        return !IsStartingUpdate && availableUpdate?.CanAutoInstall == true;
    }

    partial void OnIsCheckingUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckUpdatesButtonText));
    }

    partial void OnIsStartingUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfirmUpdateButtonText));
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        ConfirmUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedUpdateChannelOptionChanged(
        SettingsUpdateChannelOption? oldValue,
        SettingsUpdateChannelOption? newValue)
    {
        if (newValue is null)
        {
            LoadState(() => SelectedUpdateChannelOption = oldValue ?? UpdateChannelOptions[0]);
            return;
        }

        Persist(settings => settings.UpdateChannel = newValue.Channel);
    }
}
