using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Resources;

/// <summary>Chinese display metadata, independent of download/provider credentials.</summary>
public sealed class ResourceProjectLocalizer : IResourceProjectLocalizer
{
    private readonly HttpClient client;
    private readonly string cacheDirectory;
    private readonly ILogger<ResourceProjectLocalizer> logger;
    private readonly SemaphoreSlim requests = new(4);
    private readonly ConcurrentDictionary<string, Lazy<Task<ResourceProjectTranslation>>> pending = new();
    private readonly ConcurrentDictionary<string, ResourceProjectTranslation> memory = new();
    private DateTime quotaRetryAfter;

    public ResourceProjectLocalizer(LauncherPathProvider? pathProvider = null,
        ILogger<ResourceProjectLocalizer>? logger = null, HttpClient? httpClient = null)
    {
        client = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        this.logger = logger ?? NullLogger<ResourceProjectLocalizer>.Instance;
        cacheDirectory = Path.Combine((pathProvider ?? new LauncherPathProvider()).DefaultDataDirectory,
            "cache", "resource-translations-v1");
        if (!client.DefaultRequestHeaders.UserAgent.Any())
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BlockHelm-Launcher/26A17094 (+https://github.com/11451446545/BlockHelm-Launcher)");
    }

    public async Task<ResourceProjectTranslation> LocalizeAsync(ResourceProject project, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Include both source strings: edits by the author must invalidate old translations.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { Schema = 1, project.Source, project.ProjectId, project.Title, project.Description }))));
        if (memory.TryGetValue(key, out var saved))
            return saved;
        var operation = pending.GetOrAdd(key, _ => new Lazy<Task<ResourceProjectTranslation>>(
            () => ResolveAsync(project, key), LazyThreadSafetyMode.ExecutionAndPublication));
        // One canceled search must not cancel a translation used by details or another search.
        return await operation.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResourceProjectTranslation> ResolveAsync(ResourceProject project, string key)
    {
        await Task.Yield();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        string? title = IsChinese(project.Title) ? project.Title : ResourceSearchIndex.ChineseTitle(project) ?? KnownTitle(project);
        string? description = IsChinese(project.Description) ? project.Description : null;
        try
        {
            var cached = await ReadCacheAsync(key, budget.Token).ConfigureAwait(false);
            title ??= cached?.Title;
            description ??= cached?.Description;
            if (title is null || (description is null && !string.IsNullOrWhiteSpace(project.Description)))
            {
                logger.LogDebug("Translating resource metadata. Source={Source} ProjectId={ProjectId}", project.Source, project.ProjectId);
                var titleTask = title is null ? TranslateTitleAsync(project.Title, budget.Token) : Task.FromResult<string?>(title);
                var descriptionTask = description is null ? TranslateDescriptionAsync(project, budget.Token) : Task.FromResult<string?>(description);
                await Task.WhenAll(titleTask, descriptionTask).ConfigureAwait(false);
                title = await titleTask.ConfigureAwait(false);
                description = await descriptionTask.ConfigureAwait(false);
            }
            var result = new ResourceProjectTranslation(title, description);
            if (title is not null || description is not null)
                await WriteCacheAsync(key, result).ConfigureAwait(false);
            if (title is not null && (description is not null || string.IsNullOrWhiteSpace(project.Description)))
            {
                if (memory.Count >= 2048) memory.Clear();
                memory[key] = result;
            }
            logger.LogDebug("Resource translation completed. Source={Source} ProjectId={ProjectId} TitleAvailable={TitleAvailable} DescriptionAvailable={DescriptionAvailable}",
                project.Source, project.ProjectId, title is not null, description is not null);
            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogDebug("Resource translation unavailable. Source={Source} ProjectId={ProjectId} Error={Error}",
                project.Source, project.ProjectId, exception.GetType().Name);
            return new(title, description);
        }
        finally { pending.TryRemove(key, out _); }
    }

    private async Task<string?> TranslateDescriptionAsync(ResourceProject project, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project.Description)) return null;
        var source = project.Source == ResourceProjectSource.CurseForge ? "curseforge" : "modrinth";
        using var document = await GetJsonAsync($"https://mod.mcimirror.top/translate/{source}/{Uri.EscapeDataString(project.ProjectId)}", cancellationToken).ConfigureAwait(false);
        if (document is not null)
        {
            var root = document.RootElement;
            var id = Text(root, source == "curseforge" ? "modid" : "project_id");
            var original = Text(root, "original");
            var translated = Text(root, "translated");
            // Never apply a cached community translation to a different project or outdated summary.
            if (id == project.ProjectId && original == project.Description && IsChinese(translated))
                return translated;
        }
        return await TranslateAsync(project.Description, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TranslateTitleAsync(string title, CancellationToken cancellationToken)
    {
        var result = await TranslateAsync(title, cancellationToken).ConfigureAwait(false);
        if (result is not null) return result;
        // Authors often join English words (e.g. PotatoGlow); retry word boundaries, never invent a name.
        var separated = Regex.Replace(title, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|[_-]+", " ");
        return separated == title ? null : await TranslateAsync(separated, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TranslateAsync(string original, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(original) || original.Length > 8000 || DateTime.UtcNow < quotaRetryAfter)
            return null;
        var translated = new StringBuilder();
        foreach (var part in SplitText(original))
        {
            if (!part.Any(char.IsLetter)) { translated.Append(part); continue; }
            // MyMemory's public GET API accepts at most 500 UTF-8 bytes per segment.
            var uri = "https://api.mymemory.translated.net/get?langpair=en%7Czh-CN&q=" + Uri.EscapeDataString(part);
            using var document = await GetJsonAsync(uri, cancellationToken).ConfigureAwait(false);
            if (document is null) return null;
            var root = document.RootElement;
            if (root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
            {
                quotaRetryAfter = DateTime.UtcNow.AddHours(1);
                logger.LogInformation("Free resource translation quota reached; cached translations remain available.");
                return null;
            }
            if (Text(root, "responseStatus") != "200" || !root.TryGetProperty("responseData", out var data)) return null;
            var text = WebUtility.HtmlDecode(Text(data, "translatedText"));
            if (!IsChinese(text)) return null; // Untranslated names / API errors are not successful translations.
            translated.Append(text);
        }
        return IsChinese(translated.ToString()) ? translated.ToString() : null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        await requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await response.Content.LoadIntoBufferAsync(128 * 1024).WaitAsync(timeout.Token).ConfigureAwait(false);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            logger.LogDebug("Resource translation provider failed. Host={Host} Error={Error}", new Uri(uri).Host, exception.GetType().Name);
            return null;
        }
        finally { requests.Release(); }
    }

    private async Task<ResourceProjectTranslation?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(Path.Combine(cacheDirectory, key + ".json"));
            if (!file.Exists || file.Length > 64 * 1024) return null;
            var value = JsonSerializer.Deserialize<ResourceProjectTranslation>(await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false));
            return new(IsChinese(value?.Title) ? value!.Title : null, IsChinese(value?.Description) ? value!.Description : null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug("Resource translation cache miss. Error={Error}", exception.GetType().Name);
            return null;
        }
    }

    private async Task WriteCacheAsync(string key, ResourceProjectTranslation result)
    {
        var temporary = Path.Combine(cacheDirectory, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result)).ConfigureAwait(false);
            File.Move(temporary, Path.Combine(cacheDirectory, key + ".json"), true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { logger.LogDebug("Unable to persist resource translation. Error={Error}", exception.GetType().Name); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static bool IsChinese(string? value) => ResourceProjectTranslation.IsChinese(value);

    internal static IEnumerable<string> SplitText(string value)
    {
        while (Encoding.UTF8.GetByteCount(value) > 480)
        {
            var length = 0;
            var bytes = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 480) break;
                bytes += rune.Utf8SequenceLength;
                length += rune.Utf16SequenceLength;
            }
            var boundary = value.LastIndexOfAny([' ', '\n', '.', '。', '！', '？'], length - 1, length);
            if (boundary > length / 2) length = boundary + 1;
            yield return value[..length];
            value = value[length..];
        }
        if (value.Length > 0) yield return value;
    }

    private static string? KnownTitle(ResourceProject project)
    {
        // A similarly named pack must not inherit a mod's community name.
        var expectedSlug = project.Title.Trim() switch
        {
            "Sodium" => "sodium", "Lithium" => "lithium", "Iris Shaders" or "Iris" => "iris",
            "Create" => "create", "Just Enough Items (JEI)" or "Just Enough Items" => "jei",
            "Roughly Enough Items (REI)" => "rei", "JourneyMap" => "journeymap",
            "Xaero's Minimap" => "xaeros-minimap", "Xaero's World Map" => "xaeros-world-map",
            "AppleSkin" => "appleskin", "Mouse Tweaks" => "mouse-tweaks", "Inventory Profiles Next" => "inventory-profiles-next",
            "Distant Horizons" => "distanthorizons", _ => null
        };
        if (project.Kind != ResourceProjectKind.Mod || expectedSlug is null || project.Slug != expectedSlug) return null;
        return project.Title.Trim() switch
        {
            "Sodium" => "钠", "Lithium" => "锂", "Iris Shaders" or "Iris" => "鸢尾光影",
            "Create" => "机械动力", "Just Enough Items (JEI)" or "Just Enough Items" => "物品管理器",
            "Roughly Enough Items (REI)" => "大致足够的物品", "JourneyMap" => "旅行地图",
            "Xaero's Minimap" => "赛罗的小地图", "Xaero's World Map" => "赛罗的世界地图",
            "AppleSkin" => "苹果皮", "Mouse Tweaks" => "鼠标手势", "Inventory Profiles Next" => "一键背包整理",
            "Distant Horizons" => "遥远的地平线",
            _ => null
        };
    }
}
