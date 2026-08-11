/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.RegularExpressions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record NeoForgeInstallerArtifact(
    string LoaderVersion,
    string Coordinate,
    string ArtifactName,
    string Url,
    string ExpectedVersionId,
    string RuntimeLibraryCoordinate);

internal sealed record NeoForgeVersionCatalog(
    string MetadataUrl,
    string VersionPrefix,
    string? NormalizationPrefix = null,
    string? RequiredDevelopmentQualifier = null,
    bool ExcludeDevelopmentQualifiers = false)
{
    public bool Matches(string version)
    {
        if (!version.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(RequiredDevelopmentQualifier))
        {
            return version.EndsWith(
                $"+{RequiredDevelopmentQualifier}",
                StringComparison.OrdinalIgnoreCase);
        }

        return !ExcludeDevelopmentQualifiers
               || (!version.Contains("+snapshot-", StringComparison.OrdinalIgnoreCase)
                   && !version.Contains("+pre-", StringComparison.OrdinalIgnoreCase)
                   && !version.Contains("+rc-", StringComparison.OrdinalIgnoreCase));
    }

    public string NormalizeVersion(string version) =>
        !string.IsNullOrWhiteSpace(NormalizationPrefix)
        && version.StartsWith(NormalizationPrefix, StringComparison.OrdinalIgnoreCase)
            ? version[NormalizationPrefix.Length..]
            : version;
}

internal static partial class NeoForgeArtifactResolver
{
    internal const string ModernMetadataUrl =
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    internal const string Legacy1201MetadataUrl =
        "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml";

    private const string ModernArtifactBaseUrl =
        "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private const string LegacyArtifactBaseUrl =
        "https://maven.neoforged.net/releases/net/neoforged/forge";
    private const string LegacyMinecraftVersion = "1.20.1";
    private const string CraftMineVersion = "25w14craftmine";

    public static bool TryResolveCatalog(string minecraftVersion, out NeoForgeVersionCatalog catalog)
    {
        var normalizedMinecraftVersion = minecraftVersion.Trim();
        if (string.Equals(
                normalizedMinecraftVersion,
                LegacyMinecraftVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            const string versionPrefix = LegacyMinecraftVersion + "-";
            catalog = new NeoForgeVersionCatalog(
                Legacy1201MetadataUrl,
                versionPrefix,
                NormalizationPrefix: versionPrefix);
            return true;
        }

        if (string.Equals(normalizedMinecraftVersion, CraftMineVersion, StringComparison.OrdinalIgnoreCase))
        {
            catalog = new NeoForgeVersionCatalog(
                ModernMetadataUrl,
                "0.25w14craftmine.");
            return true;
        }

        var legacyReleaseMatch = LegacyReleasePattern().Match(normalizedMinecraftVersion);
        if (legacyReleaseMatch.Success)
        {
            var minor = legacyReleaseMatch.Groups["minor"].Value;
            var patch = legacyReleaseMatch.Groups["patch"].Success
                ? legacyReleaseMatch.Groups["patch"].Value
                : "0";
            catalog = new NeoForgeVersionCatalog(
                ModernMetadataUrl,
                $"{minor}.{patch}.");
            return true;
        }

        var calendarVersionMatch = CalendarVersionPattern().Match(normalizedMinecraftVersion);
        if (calendarVersionMatch.Success
            && int.TryParse(calendarVersionMatch.Groups["year"].Value, out var year)
            && year >= 26)
        {
            var release = calendarVersionMatch.Groups["release"].Value;
            var patch = calendarVersionMatch.Groups["patch"].Success
                ? calendarVersionMatch.Groups["patch"].Value
                : "0";
            var developmentPhase = calendarVersionMatch.Groups["phase"].Value;
            var developmentNumber = calendarVersionMatch.Groups["number"].Value;
            var developmentQualifier = string.IsNullOrWhiteSpace(developmentPhase)
                ? null
                : $"{developmentPhase}-{developmentNumber}";
            catalog = new NeoForgeVersionCatalog(
                ModernMetadataUrl,
                $"{year}.{release}.{patch}.",
                RequiredDevelopmentQualifier: developmentQualifier,
                ExcludeDevelopmentQualifiers: developmentQualifier is null);
            return true;
        }

        catalog = null!;
        return false;
    }

    public static NeoForgeInstallerArtifact ResolveInstaller(
        string minecraftVersion,
        string loaderVersion)
    {
        var normalizedMinecraftVersion = minecraftVersion.Trim();
        var normalizedLoaderVersion = RemoveMinecraftVersionPrefix(
            normalizedMinecraftVersion,
            loaderVersion.Trim());
        if (string.IsNullOrWhiteSpace(normalizedMinecraftVersion))
            throw new ArgumentException("Minecraft version is required.", nameof(minecraftVersion));
        if (string.IsNullOrWhiteSpace(normalizedLoaderVersion))
            throw new ArgumentException("NeoForge loader version is required.", nameof(loaderVersion));

        if (string.Equals(
                normalizedMinecraftVersion,
                LegacyMinecraftVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            var coordinate = $"{LegacyMinecraftVersion}-{normalizedLoaderVersion}";
            return new NeoForgeInstallerArtifact(
                normalizedLoaderVersion,
                coordinate,
                "forge",
                $"{LegacyArtifactBaseUrl}/{coordinate}/forge-{coordinate}-installer.jar",
                $"{LegacyMinecraftVersion}-forge-{normalizedLoaderVersion}",
                $"net.neoforged:forge:{coordinate}");
        }

        return new NeoForgeInstallerArtifact(
            normalizedLoaderVersion,
            normalizedLoaderVersion,
            "neoforge",
            $"{ModernArtifactBaseUrl}/{normalizedLoaderVersion}/neoforge-{normalizedLoaderVersion}-installer.jar",
            $"neoforge-{normalizedLoaderVersion}",
            $"net.neoforged:neoforge:{normalizedLoaderVersion}");
    }

    private static string RemoveMinecraftVersionPrefix(string minecraftVersion, string loaderVersion) =>
        loaderVersion.StartsWith(minecraftVersion + "-", StringComparison.OrdinalIgnoreCase)
            ? loaderVersion[(minecraftVersion.Length + 1)..]
            : loaderVersion;

    [GeneratedRegex(@"^1\.(?<minor>\d+)(?:\.(?<patch>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyReleasePattern();

    [GeneratedRegex(
        @"^(?<year>\d+)\.(?<release>\d+)(?:\.(?<patch>\d+))?(?:-(?<phase>snapshot|pre|rc)-(?<number>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CalendarVersionPattern();
}
