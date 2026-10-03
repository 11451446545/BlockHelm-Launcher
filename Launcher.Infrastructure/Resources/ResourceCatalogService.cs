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

using System.Net;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Resources;

public sealed class ResourceCatalogService :
    IResourceCatalogService,
    IResourceCatalogProgressReporter,
    IResourceCatalogDestinationWriter,
    IResourceThumbnailService,
    IResourceProjectLocalizer
{
    private readonly IReadOnlyDictionary<ResourceProjectSource, IResourceProviderClient> providers;
    private readonly ResourceProjectStorage storage;
    private readonly ResourceThumbnailCacheService thumbnailCache;
    private readonly McresBhlClient mcresBhlClient;
    private readonly ILogger<ResourceCatalogService> logger;
    private readonly IResourceProjectLocalizer localizer;
    private readonly ResourceSearchCoordinator searchCoordinator;

    public ResourceCatalogService(
        HttpClient? httpClient = null,
        LauncherPathProvider? pathProvider = null,
        ISettingsService? settingsService = null,
        ILogger<ResourceCatalogService>? logger = null,
        ICurseForgeApiKeyResolver? curseForgeApiKeyResolver = null,
        ILocalSaveService? localSaveService = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        IImportConcurrencyLimiter? limiter = null,
        IMcresBhlApiKeyResolver? mcresBhlApiKeyResolver = null,
        IResourceProjectLocalizer? localizer = null)
    {
        var resolvedPathProvider = pathProvider ?? new LauncherPathProvider();
        var resolvedHttpClient = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        // Catalog JSON is large; compression keeps dual-source searches responsive. Downloads retain their raw transport.
        var metadataClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        });
        this.logger = logger ?? NullLogger<ResourceCatalogService>.Instance;
        this.localizer = localizer ?? new ResourceProjectLocalizer(resolvedPathProvider);
        var keyResolver = curseForgeApiKeyResolver
            ?? new CurseForgeApiKeyResolver(resolvedPathProvider, settingsService);
        var resolvedLocalSaveService = localSaveService ?? new LocalSaveService(resolvedPathProvider);
        var resolvedMcresBhlApiKeyResolver = mcresBhlApiKeyResolver
            ?? new McresBhlApiKeyResolver(resolvedPathProvider, settingsService);

        if (!resolvedHttpClient.DefaultRequestHeaders.UserAgent.Any())
            resolvedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlockHelm-Launcher/26A17094 (+https://github.com/11451446545/BlockHelm-Launcher)");
        if (!metadataClient.DefaultRequestHeaders.UserAgent.Any())
            metadataClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlockHelm-Launcher/26A17094 (+https://github.com/11451446545/BlockHelm-Launcher)");

        var clients = new IResourceProviderClient[]
        {
            new ModrinthResourceClient(metadataClient),
            new CurseForgeResourceClient(metadataClient, keyResolver, this.logger)
        };
        providers = clients.ToDictionary(client => client.Source);
        searchCoordinator = new ResourceSearchCoordinator(providers, this.logger);
        mcresBhlClient = new McresBhlClient(
            resolvedHttpClient,
            resolvedMcresBhlApiKeyResolver,
            this.logger);
        storage = new ResourceProjectStorage(
            resolvedHttpClient,
            resolvedLocalSaveService,
            this.logger,
            settingsService,
            downloadSpeedLimitState,
            limiter);
        thumbnailCache = new ResourceThumbnailCacheService(
            resolvedPathProvider,
            resolvedHttpClient,
            limiter,
            downloadSpeedLimitState,
            this.logger);
    }

    public Task<ResourceProjectTranslation> LocalizeAsync(ResourceProject project, CancellationToken cancellationToken = default) =>
        localizer.LocalizeAsync(project, cancellationToken);

    public string? TryGetCachedThumbnailSource(ResourceProject project) =>
        thumbnailCache.TryGetCachedThumbnailSource(project);

    public Task<string?> GetOrCreateThumbnailSourceAsync(
        ResourceProject project,
        CancellationToken cancellationToken = default) =>
        thumbnailCache.GetOrCreateThumbnailSourceAsync(project, cancellationToken);

    public Task<ResourceProject?> GetProjectAsync(
        ResourceProjectReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return providers.TryGetValue(reference.Source, out var provider)
            && provider.Supports(reference.Kind)
            ? provider.GetProjectAsync(reference, cancellationToken)
            : Task.FromResult<ResourceProject?>(null);
    }

    public Task<ResourceProjectRelatedWebsite?> GetRelatedWebsiteAsync(
        ResourceProjectReference reference,
        CancellationToken cancellationToken = default) =>
        mcresBhlClient.GetRelatedWebsiteAsync(reference, cancellationToken);

    public Task<ResourceCatalogSearchResult> SearchModsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Mod,
            Query = request.Query,
            MinecraftVersion = request.MinecraftVersion,
            MinecraftVersions = request.MinecraftVersions,
            Loader = request.Loader,
            Source = request.Source,
            Category = request.Category,
            Offset = request.Offset,
            PageSize = request.PageSize,
            Continuation = request.Continuation
        }, cancellationToken);
    }

    public Task<ResourceCatalogSearchResult> SearchProjectsAsync(
        ResourceCatalogSearchRequest request,
        CancellationToken cancellationToken = default) => searchCoordinator.SearchAsync(request, cancellationToken);

    public async Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
        ResourceProjectVersionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Kind is ResourceProjectKind.Mod
            && !request.IncludeAllVersions
            && request.Loader is LoaderKind.Vanilla)
        {
            return new ResourceProjectVersionsResult();
        }

        return providers.TryGetValue(request.Source, out var provider)
            ? await provider.GetVersionsAsync(request, cancellationToken).ConfigureAwait(false)
            : new ResourceProjectVersionsResult();
    }

    public async Task<ResourceProjectDependenciesResult> GetProjectDependenciesAsync(
        ResourceProjectDependenciesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Kind is not ResourceProjectKind.Mod)
            return new ResourceProjectDependenciesResult();
        return providers.TryGetValue(request.Source, out var provider)
            ? await provider.GetDependenciesAsync(request, cancellationToken).ConfigureAwait(false)
            : new ResourceProjectDependenciesResult();
    }

    public async Task<string> InstallProjectVersionAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        var target = await storage.InstallAsync(version, instance, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Resource project version installed. VersionId={VersionId}", version.VersionId);
        logger.LogDebug("Resource project installation target resolved. VersionId={VersionId} Target={Target}", version.VersionId, target);
        return target;
    }

    public async Task<string> DownloadProjectVersionAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        var target = await storage.DownloadAsync(version, targetDirectory, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Resource project version downloaded. VersionId={VersionId}", version.VersionId);
        logger.LogDebug("Resource project download target resolved. VersionId={VersionId} Target={Target}", version.VersionId, target);
        return target;
    }

    Task<string> IResourceCatalogProgressReporter.InstallProjectVersionWithProgressAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallProjectVersionWithProgressAsync(version, instance, progress, cancellationToken);

    Task<string> IResourceCatalogProgressReporter.DownloadProjectVersionWithProgressAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken) =>
        DownloadProjectVersionWithProgressAsync(version, targetDirectory, progress, cancellationToken);

    private async Task<string> InstallProjectVersionWithProgressAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = await storage.InstallAsync(version, instance, progress, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Resource project version installed. VersionId={VersionId}", version.VersionId);
        logger.LogDebug("Resource project installation target resolved. VersionId={VersionId} Target={Target}", version.VersionId, target);
        return target;
    }

    private async Task<string> DownloadProjectVersionWithProgressAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = await storage.DownloadAsync(version, targetDirectory, progress, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Resource project version downloaded. VersionId={VersionId}", version.VersionId);
        logger.LogDebug("Resource project download target resolved. VersionId={VersionId} Target={Target}", version.VersionId, target);
        return target;
    }

    Task<ResourceProjectDestinationState> IResourceCatalogDestinationWriter.CaptureDownloadDestinationAsync(
        ResourceProjectVersion version,
        string destinationPath,
        CancellationToken cancellationToken) =>
        storage.CaptureDownloadDestinationAsync(version, destinationPath, cancellationToken);

    Task<ResourceProjectDestinationState> IResourceCatalogDestinationWriter.CaptureInstallDestinationAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        string destinationPath,
        CancellationToken cancellationToken) =>
        storage.CaptureInstallDestinationAsync(version, instance, destinationPath, cancellationToken);

    async Task<string> IResourceCatalogDestinationWriter.DownloadProjectVersionToDestinationAsync(
        ResourceProjectVersion version,
        string destinationPath,
        ResourceProjectDestinationState expectedState,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = await storage.DownloadToDestinationAsync(
                version,
                destinationPath,
                expectedState,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Resource project version downloaded. VersionId={VersionId}", version.VersionId);
        logger.LogDebug(
            "Resource project explicit download target committed. VersionId={VersionId} Target={Target}",
            version.VersionId,
            target);
        return target;
    }

    async Task<string> IResourceCatalogDestinationWriter.InstallProjectVersionToDestinationAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        string destinationPath,
        ResourceProjectDestinationState expectedState,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = await storage.InstallToDestinationAsync(
                version,
                instance,
                destinationPath,
                expectedState,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Resource project version installed. VersionId={VersionId}", version.VersionId);
        logger.LogDebug(
            "Resource project explicit install target committed. VersionId={VersionId} Target={Target}",
            version.VersionId,
            target);
        return target;
    }

    public Task<bool> ProjectVersionDownloadExistsAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default) =>
        storage.DownloadExistsAsync(version, targetDirectory, cancellationToken);

    public Task<bool> ProjectVersionInstallExistsAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken = default) =>
        storage.InstallExistsAsync(version, instance, cancellationToken);

    Task<string> IResourceCatalogDestinationWriter.EnsureInstanceContentDirectoryAsync(
        ResourceProjectKind kind,
        GameInstance instance,
        CancellationToken cancellationToken) =>
        storage.EnsureInstanceContentDirectoryAsync(kind, instance, cancellationToken);
}
