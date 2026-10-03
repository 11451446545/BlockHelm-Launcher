using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.Resources;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class ResourceProjectLocalizerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "bhl-translations-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ResourceProjectKind.Mod, ResourceProjectSource.Modrinth)]
    [InlineData(ResourceProjectKind.ResourcePack, ResourceProjectSource.CurseForge)]
    [InlineData(ResourceProjectKind.Modpack, ResourceProjectSource.Modrinth)]
    [InlineData(ResourceProjectKind.ShaderPack, ResourceProjectSource.CurseForge)]
    [InlineData(ResourceProjectKind.World, ResourceProjectSource.Modrinth)]
    public async Task NewProjectsTranslateBothFieldsAndSurviveOfflineRestart(ResourceProjectKind kind, ResourceProjectSource source)
    {
        var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.mymemory.translated.net"
            ? Machine("新冒险资源") : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var project = Project(kind, source);
        var translated = await Create(handler).LocalizeAsync(project);
        Assert.Equal("新冒险资源", translated.Title);
        Assert.Equal("新冒险资源", translated.Description);
        var offline = new Handler((_, _) => throw new HttpRequestException("offline"));
        Assert.Equal(translated, await Create(offline).LocalizeAsync(project));
        Assert.Equal(0, offline.Count);
        Assert.Equal("New Adventure", project.Title);
        Assert.Equal("7", project.ProjectId);
    }

    [Fact]
    public async Task CommunitySummaryMustMatchBothIdentityAndCurrentOriginal()
    {
        var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.Host == "mod.mcimirror.top"
            ? Json(new { project_id = "7", original = "Old description", translated = "过期译文" }) : Machine("当前简介")));
        var project = new ResourceProject { ProjectId = "7", Title = "中文名称", Description = "New description" };
        Assert.Equal("当前简介", (await Create(handler).LocalizeAsync(project)).Description);
        var changed = new ResourceProject { ProjectId = "7", Title = "中文名称", Description = "Changed again" };
        Assert.Equal("更新后的简介", (await Create(new Handler((request, _) => Task.FromResult(request.RequestUri!.Host == "mod.mcimirror.top"
            ? Json(new { project_id = "different", original = changed.Description, translated = "其他项目" }) : Machine("更新后的简介")))).LocalizeAsync(changed)).Description);
    }

    [Theory]
    [InlineData(ResourceProjectSource.Modrinth)]
    [InlineData(ResourceProjectSource.CurseForge)]
    public async Task MatchingCommunitySummaryAvoidsMachineTranslation(ResourceProjectSource source)
    {
        var handler = new Handler((request, _) => Task.FromResult(Json(source == ResourceProjectSource.Modrinth
            ? (object)new { project_id = "7", original = "New description", translated = "社区简介" }
            : new { modid = 7, original = "New description", translated = "社区简介" })));
        var result = await Create(handler).LocalizeAsync(new ResourceProject { Source = source, ProjectId = "7", Title = "已有中文", Description = "New description" });
        Assert.Equal("社区简介", result.Description);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task CancellationOfOneConsumerDoesNotPoisonSharedTranslation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) => { started.TrySetResult(); await release.Task.WaitAsync(token); return Machine("独立冒险"); });
        var localizer = Create(handler);
        var project = new ResourceProject { ProjectId = "7", Title = "New Adventure" };
        using var canceled = new CancellationTokenSource();
        var first = localizer.LocalizeAsync(project, canceled.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = localizer.LocalizeAsync(project);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        Assert.Equal("独立冒险", (await second).Title);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task UntranslatedResponsesAndFailuresAreNotPersistedAndCanBeRetried()
    {
        var succeed = false;
        var localizer = Create(new Handler((_, _) => Task.FromResult(Machine(succeed ? "新的冒险" : "New Adventure"))));
        var project = new ResourceProject { Title = "New Adventure", ProjectId = "7" };
        Assert.Null((await localizer.LocalizeAsync(project)).Title);
        Assert.Empty(Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories) : []);
        succeed = true;
        Assert.Equal("新的冒险", (await localizer.LocalizeAsync(project)).Title);
    }

    [Fact]
    public async Task QuotaResponseStopsMachineRequestsWithoutCachingErrorText()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(new { responseStatus = 200, quotaFinished = true, responseData = new { translatedText = "今日额度已经用尽" } })));
        var localizer = Create(handler);
        Assert.Null((await localizer.LocalizeAsync(new ResourceProject { Title = "First" })).Title);
        Assert.Null((await localizer.LocalizeAsync(new ResourceProject { Title = "Second" })).Title);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task CommonModNameDoesNotMislabelAnUnrelatedResourcePack()
    {
        var localizer = Create(new Handler((_, _) => Task.FromResult(Machine("创建"))));
        Assert.Equal("创建", (await localizer.LocalizeAsync(new ResourceProject { Kind = ResourceProjectKind.ResourcePack, Title = "Create", Slug = "unrelated-pack" })).Title);
        Assert.Equal("机械动力", (await localizer.LocalizeAsync(new ResourceProject { Kind = ResourceProjectKind.Mod, Title = "Create", Slug = "create", ProjectId = "known" })).Title);
    }

    [Fact]
    public async Task JoinedWordsAreTranslatedWithoutChangingOriginalName()
    {
        var handler = new Handler((request, _) => Task.FromResult(Machine(request.RequestUri!.Query.Contains("Potato%20Glow") ? "土豆辉光" : "PotatoGlow")));
        var project = new ResourceProject { Title = "PotatoGlow" };
        Assert.Equal("土豆辉光", (await Create(handler).LocalizeAsync(project)).Title);
        Assert.Equal("PotatoGlow", project.Title);
    }

    [Fact]
    public async Task CorruptCacheIsReplacedWithValidTranslation()
    {
        var project = new ResourceProject { Title = "Uncatalogued Cache Adventure XQ294" };
        await Create(new Handler((_, _) => Task.FromResult(Machine("冒险")))).LocalizeAsync(project);
        var file = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(file, "{unfinished");
        Assert.Equal("新冒险", (await Create(new Handler((_, _) => Task.FromResult(Machine("新冒险")))).LocalizeAsync(project)).Title);
        Assert.Contains("新冒险", JsonSerializer.Deserialize<ResourceProjectTranslation>(await File.ReadAllTextAsync(file))!.Title);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LongUnicodeInputStaysWithinProviderLimitAndNoRequestsExposeAuthentication()
    {
        var handler = new Handler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.DoesNotContain(request.Headers, pair => pair.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase));
            var query = Uri.UnescapeDataString(request.RequestUri!.Query.Split("&q=")[1]);
            Assert.InRange(Encoding.UTF8.GetByteCount(query), 1, 500);
            Assert.DoesNotContain('\uFFFD', query);
            return Task.FromResult(Machine("一段译文"));
        });
        var text = string.Concat(Enumerable.Repeat("Adventure 🏕️ in nature. ", 100));
        Assert.Contains("一段译文", (await Create(handler).LocalizeAsync(new ResourceProject { Title = text })).Title);
        Assert.True(handler.Count > 1);
    }

    private ResourceProjectLocalizer Create(Handler handler) => new(new LauncherPathProvider(directory, directory), httpClient: new HttpClient(handler));
    private static ResourceProject Project(ResourceProjectKind kind, ResourceProjectSource source) => new()
    { Kind = kind, Source = source, ProjectId = "7", Title = "New Adventure", Description = "Discover new worlds" };
    private static HttpResponseMessage Machine(string text) => Json(new { responseStatus = 200, responseData = new { translatedText = text } });
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        private int count;
        public int Count => count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref count); return handle(request, cancellationToken); }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
