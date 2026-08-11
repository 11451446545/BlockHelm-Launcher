/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LoaderArtifactRepairCoordinatorTests : TestTempDirectory
{
    [Theory]
    [InlineData(LoaderKind.Forge, GameFileVerificationLevel.SizeVerified)]
    [InlineData(LoaderKind.Forge, GameFileVerificationLevel.TrustedAcquisitionHash)]
    [InlineData(LoaderKind.NeoForge, GameFileVerificationLevel.SizeVerified)]
    [InlineData(LoaderKind.NeoForge, GameFileVerificationLevel.TrustedAcquisitionHash)]
    public async Task SameSizeGeneratedOutputDoesNotRequireRepair(
        LoaderKind loaderKind,
        GameFileVerificationLevel verificationLevel)
    {
        var requiresRepair = await RequiresRepairAsync(
            loaderKind,
            LoaderArtifactKind.ProcessorOutput,
            verificationLevel,
            expectedContent: "old!",
            actualContent: "new!");

        Assert.False(requiresRepair);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("different-size")]
    public async Task MissingOrWrongSizeGeneratedOutputRequiresRepair(string? actualContent)
    {
        var requiresRepair = await RequiresRepairAsync(
            LoaderKind.Forge,
            LoaderArtifactKind.ProcessorOutput,
            GameFileVerificationLevel.SizeVerified,
            expectedContent: "old!",
            actualContent);

        Assert.True(requiresRepair);
    }

    [Theory]
    [InlineData((int)LoaderArtifactKind.ProcessorOutput, GameFileVerificationLevel.HashVerified)]
    [InlineData((int)LoaderArtifactKind.RuntimeLibrary, GameFileVerificationLevel.TrustedAcquisitionHash)]
    public async Task TrustedArtifactsStillRequireRepairAfterSameSizeContentChange(
        int artifactKind,
        GameFileVerificationLevel verificationLevel)
    {
        var requiresRepair = await RequiresRepairAsync(
            LoaderKind.Forge,
            (LoaderArtifactKind)artifactKind,
            verificationLevel,
            expectedContent: "old!",
            actualContent: "new!");

        Assert.True(requiresRepair);
    }

    private async Task<bool> RequiresRepairAsync(
        LoaderKind loaderKind,
        LoaderArtifactKind artifactKind,
        GameFileVerificationLevel verificationLevel,
        string expectedContent,
        string? actualContent)
    {
        const string versionName = "Loader Verification";
        const string relativePath =
            "libraries/net/minecraft/client/1.16.5-test/client-1.16.5-test-srg.jar";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, $"{versionName}.json"), "{}");

        var artifactPath = LoaderArtifactManifestStore.ResolveManagedPath(minecraftDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        if (actualContent is not null)
            await File.WriteAllTextAsync(artifactPath, actualContent);

        var loaderVersion = loaderKind == LoaderKind.Forge ? "36.2.42" : "20.4.237";
        var identity = new GameFileLoaderIdentity(loaderKind, "1.16.5", loaderVersion);
        var manifest = new LoaderArtifactManifest(
            LoaderArtifactManifestStore.CurrentSchemaVersion,
            loaderKind,
            identity.MinecraftVersion,
            loaderVersion,
            new string('a', 64),
            [
                new LoaderArtifactManifestEntry(
                    relativePath,
                    artifactKind,
                    Source: null,
                    Sha1(expectedContent),
                    Sha256(expectedContent),
                    Encoding.UTF8.GetByteCount(expectedContent),
                    verificationLevel)
            ]);
        var manifestPath = LoaderArtifactManifestStore.GetPath(versionDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var coordinator = new LoaderArtifactRepairCoordinator([], new GameInstallCoordinator());

        return await coordinator.RequiresRepairAsync(
            minecraftDirectory,
            versionName,
            versionDirectory,
            identity,
            CancellationToken.None);
    }

    private static string Sha1(string value) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
