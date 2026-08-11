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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public enum ResourceProjectInstallationTargetKind
{
    LocalDirectory,
    ExistingInstance,
    NewModpackInstance,
    NewServerDirectory
}

public sealed record ResourceProjectInstallationRequest(
    ResourceProjectVersion Version,
    ResourceProjectInstallationTargetKind TargetKind,
    string? TargetDirectory = null,
    GameInstance? Instance = null,
    ResourceProject? Project = null,
    string? DestinationPath = null,
    ResourceProjectDestinationState? ExpectedDestinationState = null);

public sealed record ResourceProjectInstallationPreparationResult(
    bool TargetExists,
    string? TargetPath = null,
    ResourceProjectDestinationState? DestinationState = null);

public sealed record ResourceProjectDestinationState(
    bool Exists,
    long Length,
    long LastWriteTimeUtcTicks,
    string Sha256);

public enum ResourceProjectDestinationConflictReason
{
    ExistingDifferentContent,
    ChangedAfterConfirmation,
    OutsideInstanceContentDirectory,
    InvalidFileName
}

public sealed class ResourceProjectDestinationConflictException(
    string destinationPath,
    ResourceProjectDestinationConflictReason reason,
    Exception? innerException = null)
    : IOException($"Resource project destination conflict: {reason}.", innerException)
{
    public string DestinationPath { get; } = destinationPath;

    public ResourceProjectDestinationConflictReason Reason { get; } = reason;
}

public sealed record ResourceProjectInstallationResult(
    string? InstalledPath = null,
    ModpackImportResult? ModpackImportResult = null);

public sealed class ResourceProjectDistributionRestrictedException(string versionId, Exception? innerException = null)
    : Exception($"Third-party download is restricted for resource project version {versionId}.", innerException)
{
    public string VersionId { get; } = versionId;
}

public interface IResourceProjectInstallationService
{
    Task CleanupStaleWorkspacesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task<string> EnsureInstanceContentDirectoryAsync(
        ResourceProjectKind kind,
        GameInstance instance,
        CancellationToken cancellationToken = default);

    Task<ResourceProjectInstallationPreparationResult> PrepareAsync(
        ResourceProjectInstallationRequest request,
        CancellationToken cancellationToken = default);

    Task<ResourceProjectInstallationResult> ExecuteAsync(
        ResourceProjectInstallationRequest request,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
