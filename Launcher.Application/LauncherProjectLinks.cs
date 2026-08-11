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

namespace Launcher.Application;

public static class LauncherProjectLinks
{
    public const string GitHubOwner = "11451446545";
    public const string GitHubRepositoryName = "BlockHelm-Launcher";
    public const string GitHubRepositoryUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepositoryName;
    public const string GitHubLicenseUrl = GitHubRepositoryUrl + "/blob/master/LICENSE";
    public const string UserAgreementUrl = "https://docs.qq.com/doc/DQXV0Y2RERUJIYm9L";
    public const string MinecraftPurchaseUrl = "https://www.minecraft.net/store/minecraft-java-bedrock-edition-pc";
    public const string GitHubFeatureSuggestionsUrl = "https://wj.qq.com/s2/27542397/j0y8";
    public const string GitHubIssuesUrl = "https://wj.qq.com/s2/27542401/5pt0";
    public const string GitHubReleasesUrl = GitHubRepositoryUrl + "/releases";
    public const string GitHubReleasesApiUrl = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepositoryName + "/releases";
    public const string GitHubUserAgent = "BlockHelm-Launcher";
    public const string GitHubUpdateManifestUrlTemplate = "https://raw.githubusercontent.com/" + GitHubOwner + "/" + GitHubRepositoryName + "/update-manifests/update/{0}/latest.json";
}
