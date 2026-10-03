using System.Text.Json;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Resources;
using Microsoft.Extensions.Logging;

if (args.Length != 1) throw new ArgumentException("Supply an isolated output directory.");
var directory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(directory);
var service = new ResourceCatalogService(pathProvider: new LauncherPathProvider(directory, directory), curseForgeApiKeyResolver: new NoKey(), logger: new ProbeLogger());
var jsonOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
foreach (var query in new[] { "机械动力", "物品管理器", "暮色森林" })
{
    var result = await service.SearchProjectsAsync(new() { Query = query, PageSize = 40 });
    Console.WriteLine(JsonSerializer.Serialize(new { query, Count = result.Projects.Count, result.IsCurseForgeUnavailable,
        result.IsModrinthUnavailable, result.UsesCurseForgeMirror, Sources = result.Projects.GroupBy(p => p.Source).Select(g => new { Source = g.Key.ToString(), Count = g.Count() }),
        Top = result.Projects.Take(6).Select(p => new { p.Title, p.Slug, Source = p.Source.ToString() }) }, jsonOptions));
    if (result.IsCurseForgeUnavailable || result.IsModrinthUnavailable || result.Projects.Select(p => p.Source).Distinct().Count() < 2)
        throw new Exception("Both sources must return results for the live Chinese search.");
}
var first = await service.SearchProjectsAsync(new() { PageSize = 40 });
var second = await service.SearchProjectsAsync(new() { PageSize = 40, Continuation = first.Continuation });
Console.WriteLine(JsonSerializer.Serialize(new { FirstPage = first.Projects.Count, SecondPage = second.Projects.Count,
    Unique = first.Projects.Concat(second.Projects).DistinctBy(p => (p.Source, p.ProjectId)).Count(), second.HasMore }));
var versions = await service.GetProjectVersionsAsync(new() { Source = ResourceProjectSource.CurseForge, ProjectId = "238222", IncludeAllVersions = true, PageSize = 1 });
var version = versions.Versions.First();
var file = await service.DownloadProjectVersionAsync(version, directory);
Console.WriteLine(JsonSerializer.Serialize(new { Downloaded = Path.GetFileName(file), Bytes = new FileInfo(file).Length,
    Hashes = version.FileHashes.Count, Dependencies = version.RequiredDependencies.Count, Source = "CurseForge via MCIM" }));

sealed class NoKey : ICurseForgeApiKeyResolver
{
    public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}

sealed class ProbeLogger : ILogger<ResourceCatalogService>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(level)) Console.WriteLine(formatter(state, exception));
    }
}
