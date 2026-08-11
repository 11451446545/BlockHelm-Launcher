/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

/// <summary>
/// Removes user-owned files and directories, preferring a recoverable system
/// recycle-bin operation before falling back to permanent deletion.
/// </summary>
public interface IUserFileDeletionService
{
    void DeleteFile(string path);

    void DeleteDirectory(string path);
}
