/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Resources;

/// <summary>
/// Infrastructure-only capability for preparing and writing resource destinations.
/// </summary>
internal interface IResourceCatalogDestinationWriter
{
    Task<string> EnsureInstanceContentDirectoryAsync(
        ResourceProjectKind kind,
        GameInstance instance,
        CancellationToken cancellationToken);

    Task<ResourceProjectDestinationState> CaptureDownloadDestinationAsync(
        ResourceProjectVersion version,
        string destinationPath,
        CancellationToken cancellationToken);

    Task<ResourceProjectDestinationState> CaptureInstallDestinationAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        string destinationPath,
        CancellationToken cancellationToken);

    Task<string> DownloadProjectVersionToDestinationAsync(
        ResourceProjectVersion version,
        string destinationPath,
        ResourceProjectDestinationState expectedDestinationState,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken);

    Task<string> InstallProjectVersionToDestinationAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        string destinationPath,
        ResourceProjectDestinationState expectedDestinationState,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken);
}
