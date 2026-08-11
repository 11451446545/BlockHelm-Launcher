/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.ComponentModel;
using System.IO;
using System.Security;
using Launcher.Application.Services;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class UserFileDeletionService : IUserFileDeletionService
{
    private readonly Action<string> recyclePath;
    private readonly Action<string> permanentlyDeleteFile;
    private readonly Action<string, bool> permanentlyDeleteDirectory;
    private readonly ILogger<UserFileDeletionService> logger;

    public UserFileDeletionService(ILogger<UserFileDeletionService>? logger = null)
        : this(
            WindowsRecycleBin.MovePath,
            File.Delete,
            Directory.Delete,
            logger)
    {
    }

    internal UserFileDeletionService(
        Action<string> recyclePath,
        Action<string> permanentlyDeleteFile,
        Action<string, bool> permanentlyDeleteDirectory,
        ILogger<UserFileDeletionService>? logger = null)
    {
        this.recyclePath = recyclePath;
        this.permanentlyDeleteFile = permanentlyDeleteFile;
        this.permanentlyDeleteDirectory = permanentlyDeleteDirectory;
        this.logger = logger ?? NullLogger<UserFileDeletionService>.Instance;
    }

    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = Path.GetFullPath(path);
        if (!File.Exists(normalizedPath))
            return;

        if (TryRecycle(normalizedPath, "file") || !File.Exists(normalizedPath))
            return;

        permanentlyDeleteFile(normalizedPath);
        logger.LogDebug(
            "User file permanently deleted after recycle-bin fallback. FileName={FileName}",
            Path.GetFileName(normalizedPath));
    }

    public void DeleteDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = Path.GetFullPath(path);
        if (!Directory.Exists(normalizedPath))
            return;

        if (TryRecycle(normalizedPath, "directory") || !Directory.Exists(normalizedPath))
            return;

        permanentlyDeleteDirectory(normalizedPath, true);
        logger.LogDebug(
            "User directory permanently deleted after recycle-bin fallback. DirectoryName={DirectoryName}",
            Path.GetFileName(normalizedPath));
    }

    private bool TryRecycle(string path, string pathKind)
    {
        try
        {
            recyclePath(path);
            logger.LogDebug(
                "User path moved to recycle bin. PathKind={PathKind} Name={Name}",
                pathKind,
                Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (IsRecycleBinFailure(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to move user path to recycle bin; permanent deletion will be attempted. PathKind={PathKind} Name={Name}",
                pathKind,
                Path.GetFileName(path));
            return false;
        }
    }

    private static bool IsRecycleBinFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or Win32Exception
            or PlatformNotSupportedException;
}
