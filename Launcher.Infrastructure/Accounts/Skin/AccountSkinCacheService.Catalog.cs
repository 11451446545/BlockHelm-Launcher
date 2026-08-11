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

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

internal sealed partial class AccountSkinCacheService
{
public IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account)
    {
        return GetLibrarySkins(account);
    }

    internal void MigrateAccountSkinsToLibrary(
        LauncherAccount account,
        CancellationToken cancellationToken)
    {
        var uuid = account.Uuid ?? account.Id;
        if (string.IsNullOrWhiteSpace(uuid))
            return;

        foreach (var skin in account.SkinLibrary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyExistingSkinIntoLibrary(account, skin);
        }

        TryCopyLegacyAccountDirectoryIntoLibrary(account, uuid, cancellationToken);
    }

    internal IReadOnlyList<LauncherSkinRecord> GetSharedLibrarySkins() =>
        GetLibrarySkins(
            GetSharedLibrarySkinDirectory(),
            [],
            useContentIdentity: true);

    internal IReadOnlyList<LauncherSkinRecord> GetLibrarySkins(LauncherAccount account)
    {
        var libraryDirectory = GetLibrarySkinDirectory(account);
        return GetLibrarySkins(
            libraryDirectory,
            account.SkinLibrary,
            useContentIdentity: !account.IsThirdParty);
    }

    private IReadOnlyList<LauncherSkinRecord> GetLibrarySkins(
        string libraryDirectory,
        IReadOnlyList<LauncherSkinRecord> knownSkins,
        bool useContentIdentity)
    {
        if (!Directory.Exists(libraryDirectory))
            return [];

        return Directory.EnumerateFiles(libraryDirectory, "*.png")
            .Select(path => TryCreateRecordForFile(
                knownSkins,
                path,
                useContentIdentity))
            .Where(record => record is not null)
            .Select(record => record!)
            .OrderBy(record => record.AddedAtUtc)
            .ToList();
    }

    internal LauncherSkinRecord? CopyExistingSkinIntoLibrary(
        LauncherAccount account,
        LauncherSkinRecord skin)
    {
        // 历史版本按账户存放皮肤；复制到共享目录而非移动，保证迁移可回退。
        var sourcePath = ResolveSkinSourcePath(skin.Source);
        if (sourcePath is null || !File.Exists(sourcePath))
            return null;

        var libraryDirectory = Path.GetFullPath(GetLibrarySkinDirectory(account));
        var hash = ComputeSkinContentHash(File.ReadAllBytes(sourcePath));
        if (IsPathInDirectory(Path.GetFullPath(sourcePath), libraryDirectory))
            return CreateRecord(hash, skin.SkinModel, new Uri(sourcePath).AbsoluteUri);

        var targetPath = CreateLibrarySkinPath(account, hash, skin.SkinModel);
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            return CreateRecord(hash, skin.SkinModel, new Uri(targetPath).AbsoluteUri);

        if (!File.Exists(targetPath))
            File.Copy(sourcePath, targetPath);
        return CreateRecord(hash, skin.SkinModel, new Uri(targetPath).AbsoluteUri);
    }

    private void TryCopyLegacyAccountDirectoryIntoLibrary(
        LauncherAccount account,
        string uuid,
        CancellationToken cancellationToken)
    {
        if (account.IsThirdParty)
            return;

        var legacyDirectory = GetAccountSkinDirectory(uuid);
        var libraryDirectory = GetLibrarySkinDirectory(account);
        if (!Directory.Exists(legacyDirectory)
            || string.Equals(
                Path.GetFullPath(legacyDirectory),
                Path.GetFullPath(libraryDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var legacyPath in Directory.EnumerateFiles(legacyDirectory, "*.png"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = TryCreateRecordForFile(
                account.SkinLibrary,
                legacyPath,
                useContentIdentity: true);
            if (record is null)
                continue;

            var targetPath = CreateLibrarySkinPath(account, record.ContentHash, record.SkinModel);
            if (!File.Exists(targetPath))
                File.Copy(legacyPath, targetPath);
        }
    }

    private LauncherSkinRecord? TryCreateRecordForFile(
        IReadOnlyList<LauncherSkinRecord> skins,
        string skinPath,
        bool useContentIdentity = false)
    {
        try
        {
            var hash = ComputeSkinContentHash(File.ReadAllBytes(skinPath));
            var skinModel = TryParseSkinModel(skinPath) ?? FindModelForFile(skins, skinPath, hash) ?? MinecraftSkinModel.Classic;
            var existing = FindExisting(skins, hash, skinModel)
                ?? FindExistingBySource(skins, skinPath);
            return existing is null || useContentIdentity
                ? CreateRecord(hash, skinModel, new Uri(skinPath).AbsoluteUri)
                : CopyRecordWithSource(existing, new Uri(skinPath).AbsoluteUri, hash);
        }
        catch
        {
            return null;
        }
    }
}
