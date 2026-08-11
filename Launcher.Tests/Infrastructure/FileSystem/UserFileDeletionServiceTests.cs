/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class UserFileDeletionServiceTests : TestTempDirectory
{
    [Fact]
    public void DeleteFileUsesRecycleBinWithoutPermanentDeletion()
    {
        Directory.CreateDirectory(TempRoot);
        var source = Path.Combine(TempRoot, "user.jar");
        var recycled = Path.Combine(TempRoot, "recycled.jar");
        File.WriteAllText(source, "content");
        var permanentDeleteCalls = 0;
        var service = new UserFileDeletionService(
            path => File.Move(path, recycled),
            _ => permanentDeleteCalls++,
            (_, _) => throw new InvalidOperationException());

        service.DeleteFile(source);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(recycled));
        Assert.Equal(0, permanentDeleteCalls);
    }

    [Fact]
    public void DeleteDirectoryUsesRecycleBinWithoutPermanentDeletion()
    {
        Directory.CreateDirectory(TempRoot);
        var source = Path.Combine(TempRoot, "world");
        var recycled = Path.Combine(TempRoot, "recycled-world");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "level.dat"), "content");
        var permanentDeleteCalls = 0;
        var service = new UserFileDeletionService(
            path => Directory.Move(path, recycled),
            _ => throw new InvalidOperationException(),
            (_, _) => permanentDeleteCalls++);

        service.DeleteDirectory(source);

        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Combine(recycled, "level.dat")));
        Assert.Equal(0, permanentDeleteCalls);
    }

    [Fact]
    public void DeleteFileFallsBackToPermanentDeletionWhenRecycleBinFails()
    {
        Directory.CreateDirectory(TempRoot);
        var source = Path.Combine(TempRoot, "backup.zip");
        File.WriteAllText(source, "content");
        var permanentDeleteCalls = 0;
        var service = new UserFileDeletionService(
            _ => throw new IOException("recycle unavailable"),
            path =>
            {
                permanentDeleteCalls++;
                File.Delete(path);
            },
            (_, _) => throw new InvalidOperationException());

        service.DeleteFile(source);

        Assert.False(File.Exists(source));
        Assert.Equal(1, permanentDeleteCalls);
    }

    [Fact]
    public void DeleteDirectoryFallsBackToPermanentDeletionWhenRecycleBinFails()
    {
        Directory.CreateDirectory(TempRoot);
        var source = Path.Combine(TempRoot, "save");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "level.dat"), "content");
        var permanentDeleteCalls = 0;
        var service = new UserFileDeletionService(
            _ => throw new IOException("recycle unavailable"),
            _ => throw new InvalidOperationException(),
            (path, recursive) =>
            {
                permanentDeleteCalls++;
                Directory.Delete(path, recursive);
            });

        service.DeleteDirectory(source);

        Assert.False(Directory.Exists(source));
        Assert.Equal(1, permanentDeleteCalls);
    }

    [Fact]
    public void DeleteFileSurfacesPermanentDeletionFailure()
    {
        Directory.CreateDirectory(TempRoot);
        var source = Path.Combine(TempRoot, "locked.zip");
        File.WriteAllText(source, "content");
        var service = new UserFileDeletionService(
            _ => throw new IOException("recycle unavailable"),
            _ => throw new UnauthorizedAccessException("locked"),
            (_, _) => throw new InvalidOperationException());

        Assert.Throws<UnauthorizedAccessException>(() => service.DeleteFile(source));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MissingPathsAreIdempotentAndDoNotInvokeDeletion()
    {
        Directory.CreateDirectory(TempRoot);
        var service = new UserFileDeletionService(
            _ => throw new InvalidOperationException("must not recycle"),
            _ => throw new InvalidOperationException("must not delete file"),
            (_, _) => throw new InvalidOperationException("must not delete directory"));

        service.DeleteFile(Path.Combine(TempRoot, "missing.file"));
        service.DeleteDirectory(Path.Combine(TempRoot, "missing-directory"));
    }
}
