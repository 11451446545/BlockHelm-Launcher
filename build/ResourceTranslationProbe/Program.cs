using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.Resources;

if (args.Length != 1) throw new ArgumentException("Supply an isolated cache directory for this live probe.");
var paths = new LauncherPathProvider(Path.GetFullPath(args[0]), Path.GetFullPath(args[0]));
var localizer = new ResourceProjectLocalizer(paths);
using var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd("BlockHelm-Launcher/26A17094");
foreach (var (kind, facet) in new[] { (ResourceProjectKind.Mod, "mod"), (ResourceProjectKind.ResourcePack, "resourcepack"),
    (ResourceProjectKind.Modpack, "modpack"), (ResourceProjectKind.ShaderPack, "shader") })
{
    var facets = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { new[] { "project_type:" + facet } }));
    using var document = JsonDocument.Parse(await client.GetStringAsync("https://api.modrinth.com/v2/search?limit=1&index=newest&facets=" + facets));
    var hit = document.RootElement.GetProperty("hits")[0];
    var project = new ResourceProject { Kind = kind, Source = ResourceProjectSource.Modrinth,
        ProjectId = hit.GetProperty("project_id").GetString()!, Title = hit.GetProperty("title").GetString()!, Description = hit.GetProperty("description").GetString()! };
    var stopwatch = Stopwatch.StartNew();
    var result = await localizer.LocalizeAsync(project);
    Console.WriteLine(JsonSerializer.Serialize(new { kind, project.ProjectId, OriginalTitle = project.Title, result.Title, result.Description, Milliseconds = stopwatch.ElapsedMilliseconds },
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}
