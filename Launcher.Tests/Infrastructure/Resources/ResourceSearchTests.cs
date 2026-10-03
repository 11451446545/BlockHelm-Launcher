using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Resources;
using Xunit;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class ResourceSearchTests
{
    [Theory]
    [InlineData(null, "mod.mcimirror.top")]
    [InlineData("test-key", "api.curseforge.com")]
    public async Task CurseForgeSearchAndFilesUseTheSelectedEndpointWithoutLeakingKeys(string? key, string host)
    {
        var handler = new Handler(request =>
        {
            Assert.Equal(host, request.RequestUri!.Host);
            Assert.Equal(key is not null, request.Headers.Contains("x-api-key"));
            if (key is not null) Assert.Equal(key, Assert.Single(request.Headers.GetValues("x-api-key")));
            return Json(request.RequestUri.AbsolutePath.Contains("/files")
                ? """{"data":[{"id":1001,"fileName":"test.jar","downloadUrl":"https://edge.forgecdn.net/test.jar","fileLength":4,"hashes":[{"algo":1,"value":"0123456789012345678901234567890123456789"}]}],"pagination":{"totalCount":1}}"""
                : """{"data":[{"id":238222,"slug":"jei","name":"Just Enough Items (JEI)"}],"pagination":{"totalCount":1}}""");
        });
        var service = Create(handler, key);
        var result = await service.SearchProjectsAsync(new() { Source = ResourceProjectSource.CurseForge, Query = "jei" });
        Assert.Equal("238222", Assert.Single(result.Projects).ProjectId);
        Assert.False(result.IsCurseForgeUnavailable);
        Assert.Equal(key is null, result.UsesCurseForgeMirror);
        var files = await service.GetProjectVersionsAsync(new() { Source = ResourceProjectSource.CurseForge, ProjectId = "238222", IncludeAllVersions = true });
        Assert.NotEmpty(Assert.Single(files.Versions).FileHashes);
    }

    [Fact]
    public async Task ChineseSearchExpandsKnownNamesKeepsRawQueryAndRanksTheBaseProjectFirst()
    {
        var handler = new Handler(request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("index=relevance", query);
            return Json(query.Contains("query=Create&")
                ? """{"hits":[{"project_id":"addon","slug":"create-addon","title":"Create Addon","downloads":900},{"project_id":"base","slug":"create","title":"Create","downloads":2}],"total_hits":2}"""
                : """{"hits":[{"project_id":"new","slug":"new","title":"机械动力新附属"}],"total_hits":1}""");
        });
        var result = await Create(handler).SearchProjectsAsync(new() { Query = "机械动力", Source = ResourceProjectSource.Modrinth });
        Assert.Equal("base", result.Projects[0].ProjectId);
        Assert.Contains(result.Projects, project => project.ProjectId == "new");
        Assert.Contains(handler.Uris, uri => Uri.UnescapeDataString(uri.Query).Contains("query=机械动力&"));
    }

    [Fact]
    public async Task EachPlatformAdvancesIndependentlyAndFinishedSourcesAreNotFetchedAgain()
    {
        var handler = new Handler(request => request.RequestUri!.Host == "api.modrinth.com"
            ? Json(request.RequestUri.Query.Contains("offset=0&")
                ? """{"hits":[{"project_id":"one","title":"one"}],"total_hits":41}"""
                : """{"hits":[{"project_id":"two","title":"two"}],"total_hits":41}""")
            : Json("""{"data":[{"id":1,"name":"first"}],"pagination":{"totalCount":1}}"""));
        var service = Create(handler);
        var first = await service.SearchProjectsAsync(new() { PageSize = 40 });
        var cursor = Assert.Single(first.Continuation);
        Assert.Equal(ResourceProjectSource.Modrinth, cursor.Source);
        Assert.Equal(40, cursor.Offset);
        var second = await service.SearchProjectsAsync(new() { PageSize = 40, Continuation = first.Continuation });
        Assert.Equal("two", Assert.Single(second.Projects).ProjectId);
        Assert.False(second.HasMore);
        Assert.Equal(3, handler.Uris.Count);
        var finished = await service.SearchProjectsAsync(new() { Continuation = second.Continuation });
        Assert.Empty(finished.Projects);
        Assert.Equal(3, handler.Uris.Count);
    }

    [Theory]
    [InlineData("api.modrinth.com", ResourceProjectSource.CurseForge)]
    [InlineData("mod.mcimirror.top", ResourceProjectSource.Modrinth)]
    public async Task OneUnavailableSourceDoesNotDiscardTheOther(string failedHost, ResourceProjectSource remaining)
    {
        var handler = new Handler(request => request.RequestUri!.Host == failedHost
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : request.RequestUri.Host == "api.modrinth.com"
                ? Json("""{"hits":[{"project_id":"one","title":"one"}],"total_hits":1}""")
                : Json("""{"data":[{"id":1,"name":"first"}],"pagination":{"totalCount":1}}"""));
        var result = await Create(handler).SearchProjectsAsync(new());
        Assert.Equal(remaining, Assert.Single(result.Projects).Source);
        Assert.Equal(failedHost == "api.modrinth.com", result.IsModrinthUnavailable);
        Assert.Equal(failedHost == "mod.mcimirror.top", result.IsCurseForgeUnavailable);
    }

    [Theory]
    [InlineData(ResourceProjectKind.Mod, "机械动力", "Create")]
    [InlineData(ResourceProjectKind.Mod, "物品管理器", "Just Enough Items")]
    [InlineData(ResourceProjectKind.Modpack, "格雷科技：新视野", "GT: New Horizons")]
    public void CommunityAliasesExpandWithoutDiscardingTheOriginalQuery(ResourceProjectKind kind, string query, string expected)
    {
        Assert.Contains(expected, ResourceSearchIndex.Expand(kind, query));
        Assert.Contains(query, ResourceSearchIndex.Expand(kind, query));
        Assert.Equal(new[] { "新上传的未收录项目" }, ResourceSearchIndex.Expand(kind, "新上传的未收录项目"));
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsAnUnavailableSource()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new Handler(_ => throw new Exception()))
            .SearchProjectsAsync(new(), canceled.Token));
    }

    private static ResourceCatalogService Create(HttpMessageHandler handler, string? key = null) =>
        new(new HttpClient(handler), curseForgeApiKeyResolver: new Keys(key));
    private sealed class Keys(string? key) : ICurseForgeApiKeyResolver
    {
        public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(key);
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public ConcurrentBag<Uri> Uris { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uris.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
