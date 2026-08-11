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

namespace Launcher.Application.Accounts;

public interface IAccountSkinLibraryService
{
    IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account);

    IReadOnlyList<LauncherSkinRecord> GetSharedSkins();

    Task MigrateLegacySkinsAsync(
        IReadOnlyList<LauncherAccount> accounts,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, LauncherSkinRecord>> SyncMicrosoftAccountSkinsAsync(
        IReadOnlyList<LauncherAccount> accounts,
        CancellationToken cancellationToken = default);

    Task<LauncherSkinRecord> ImportSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken = default);

    Task<string?> CreateAvatarSourceAsync(
        LauncherAccount account,
        LauncherSkinRecord skin,
        CancellationToken cancellationToken = default);

    Task DeleteSkinAsync(
        LauncherAccount account,
        LauncherSkinRecord skin,
        CancellationToken cancellationToken = default);
}
