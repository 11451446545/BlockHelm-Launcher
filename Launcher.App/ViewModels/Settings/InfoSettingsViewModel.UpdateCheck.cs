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

using Launcher.App.Resources;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class InfoSettingsViewModel
{
    private async Task CheckUpdatesCoreAsync(UpdateCheckPresentation presentation)
    {
        if (isUpdateCheckRunning)
            return;

        var channel = SelectedUpdateChannelOption?.Channel ?? LauncherDefaults.DefaultUpdateChannel;
        isUpdateCheckRunning = true;
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        if (presentation is UpdateCheckPresentation.Manual)
        {
            IsCheckingUpdates = true;
            statusService.Report(Strings.Status_CheckingUpdates);
        }

        try
        {
            logger.LogInformation(
                "Launcher update check started. CurrentVersion={CurrentVersion} Channel={Channel} Presentation={Presentation}",
                LauncherVersionText,
                channel,
                presentation);

            var result = await launcherUpdateService.CheckForUpdatesAsync(
                LauncherVersionText,
                channel);

            if (result.IsFailed)
            {
                logger.LogWarning(
                    "Launcher update check failed. CurrentVersion={CurrentVersion} Channel={Channel} Reason={Reason}",
                    LauncherVersionText,
                    channel,
                    result.ErrorMessage ?? "unknown");
                if (presentation is UpdateCheckPresentation.Manual)
                    statusService.Report(Strings.Status_CheckUpdatesFailed);
                return;
            }

            if (!result.IsUpdateAvailable || result.Update is null)
            {
                logger.LogInformation(
                    "Launcher update check completed with no update. CurrentVersion={CurrentVersion} Channel={Channel}",
                    LauncherVersionText,
                    channel);
                if (presentation is UpdateCheckPresentation.Manual)
                    statusService.Report(Strings.Status_LauncherAlreadyLatest);
                return;
            }

            ShowUpdateAvailableDialog(result.Update);
            logger.LogInformation(
                "Launcher update is available. CurrentVersion={CurrentVersion} NewVersion={NewVersion} Channel={Channel}",
                LauncherVersionText,
                result.Update.Version,
                channel);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Launcher update check threw an exception. CurrentVersion={CurrentVersion} Channel={Channel}",
                LauncherVersionText,
                channel);
            if (presentation is UpdateCheckPresentation.Manual)
                statusService.Report(Strings.Status_CheckUpdatesFailed);
        }
        finally
        {
            if (presentation is UpdateCheckPresentation.Manual)
                IsCheckingUpdates = false;
            isUpdateCheckRunning = false;
            CheckUpdatesCommand.NotifyCanExecuteChanged();
        }
    }
}
