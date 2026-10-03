using System.Diagnostics;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Resources;

internal sealed class ResourceSearchCoordinator(
    IReadOnlyDictionary<ResourceProjectSource, IResourceProviderClient> providers, ILogger logger)
{
    private readonly SemaphoreSlim requests = new(4);

    public async Task<ResourceCatalogSearchResult> SearchAsync(ResourceCatalogSearchRequest request, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var selected = providers.Values.Where(provider => provider.Supports(request.Kind)
            && (!request.Source.HasValue || request.Source == provider.Source)).ToArray();
        var queries = ResourceSearchIndex.Expand(request.Kind, request.Query);
        var pages = request.Continuation ?? selected.SelectMany(provider => queries.Select(query =>
            new ResourceSearchCursor(provider.Source, query, Math.Max(0, request.Offset)))).ToArray();
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var results = await Task.WhenAll(pages.Where(page => selected.Any(provider => provider.Source == page.Source))
            .Select(async page =>
            {
                await requests.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var result = await providers[page.Source].SearchAsync(new ResourceCatalogSearchRequest
                    {
                        Kind = request.Kind, Query = page.Query, Source = page.Source,
                        MinecraftVersion = request.MinecraftVersion, MinecraftVersions = request.MinecraftVersions,
                        Loader = request.Loader, Category = request.Category, Offset = page.Offset, PageSize = pageSize
                    }, timeout.Token).ConfigureAwait(false);
                    return (Page: page, Result: result);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    logger.LogWarning(exception, "Resource search source unavailable. Source={Source} Error={Error}", page.Source, exception.GetType().Name);
                    return (Page: page, Result: new ResourceProviderSearchResult([], false, true));
                }
                finally { requests.Release(); }
            })).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var ranked = results.SelectMany((result, queryOrder) => result.Result.Projects.Select((project, rank) =>
            (Project: project, Rank: rank, QueryOrder: queryOrder, Exact: ResourceSearchIndex.IsExactName(project, result.Page.Query))));
        var ordered = string.IsNullOrWhiteSpace(request.Query)
            ? ranked.OrderByDescending(value => value.Project.Downloads).ThenBy(value => value.QueryOrder)
            : ranked.OrderByDescending(value => value.Exact).ThenBy(value => value.Rank).ThenBy(value => value.QueryOrder);
        var projects = ordered.DistinctBy(value => (value.Project.Source, value.Project.ProjectId)).Select(value => value.Project).ToArray();
        var next = results.Where(result => result.Result.HasMore && !result.Result.IsUnavailable)
            .Select(result => result.Page with { Offset = result.Page.Offset + pageSize }).ToArray();
        logger.LogInformation("Resource search completed. Kind={Kind} Sources={Sources} Queries={Queries} Results={Results} ElapsedMilliseconds={ElapsedMilliseconds}",
            request.Kind, selected.Length, queries.Count, projects.Length, stopwatch.ElapsedMilliseconds);
        return new ResourceCatalogSearchResult
        {
            Projects = projects, HasMore = next.Length > 0, Continuation = next,
            IsCurseForgeUnavailable = results.Any(result => result.Page.Source == ResourceProjectSource.CurseForge && result.Result.IsUnavailable),
            IsModrinthUnavailable = results.Any(result => result.Page.Source == ResourceProjectSource.Modrinth && result.Result.IsUnavailable),
            UsesCurseForgeMirror = results.Any(result => result.Result.UsesMirror)
        };
    }
}
