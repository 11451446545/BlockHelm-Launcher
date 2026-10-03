using System.IO;
using System.Text;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Resources;

/// <summary>Offline community names expand queries; unknown queries still reach the platforms.</summary>
internal static class ResourceSearchIndex
{
    private sealed record Entry(string Slug, string Chinese, string English, string Abbreviation);
    private static readonly Lazy<IReadOnlyList<Entry>> Mods = new(() => Read("mod_data.txt"));
    private static readonly Lazy<IReadOnlyList<Entry>> Modpacks = new(() => Read("modpack_data.txt"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ModNames = new(() => Names(Mods.Value));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ModpackNames = new(() => Names(Modpacks.Value));

    internal static IReadOnlyList<string> Expand(ResourceProjectKind kind, string query)
    {
        query = query.Trim();
        if (!ResourceProjectTranslation.IsChinese(query)) return [query];
        var normalized = Normalize(query);
        if (normalized.Length == 0) return [query];
        var scored = Entries(kind)
            .Select(entry => (Entry: entry, Score: Score(entry, normalized)))
            .Where(value => value.Score > 0 && !string.IsNullOrWhiteSpace(value.Entry.English))
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Entry.Chinese.Length).ToArray();
        var matches = scored.Where(value => scored[0].Score < 1000 || value.Score == 1000)
            .Select(value => value.Entry.English);
        // Common generic searches should find their familiar primary project first.
        var preferred = kind == ResourceProjectKind.Mod && normalized == "物品管理器"
            ? new[] { "Just Enough Items" } : Array.Empty<string>();
        return preferred.Concat(matches).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)
            .Append(query).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string? ChineseTitle(ResourceProject project)
    {
        var title = Normalize(project.Title.Split('(', '（')[0]);
        if (title.Length == 0) return null;
        // Require the original name to agree; equal slugs on different platforms are not identity proof.
        var names = project.Kind switch { ResourceProjectKind.Mod => ModNames.Value,
            ResourceProjectKind.Modpack => ModpackNames.Value, _ => null };
        return names is not null && names.TryGetValue(title, out var chinese) ? chinese : null;
    }

    internal static bool IsExactName(ResourceProject project, string query)
    {
        var text = Normalize(query);
        return text.Length > 0 && (Normalize(project.Title.Split('(', '（')[0]) == text
            || Normalize(project.Slug) == text || Normalize(project.Title) == text);
    }

    private static int Score(Entry entry, string query)
    {
        var chinese = Normalize(entry.Chinese);
        if (chinese == query || Normalize(entry.Abbreviation) == query) return 1000;
        return chinese.Contains(query, StringComparison.Ordinal) ? 500 - Math.Min(400, chinese.Length - query.Length) : 0;
    }

    private static IReadOnlyList<Entry> Entries(ResourceProjectKind kind) => kind switch
    {
        ResourceProjectKind.Mod => Mods.Value,
        ResourceProjectKind.Modpack => Modpacks.Value,
        _ => []
    };

    private static IReadOnlyList<Entry> Read(string name)
    {
        using var stream = typeof(ResourceSearchIndex).Assembly.GetManifestResourceStream(
            "Launcher.Infrastructure.Resources.SearchData." + name);
        if (stream is null) return [];
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var entries = new List<Entry>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var fields = line.Split(';');
            if (fields.Length >= 6) entries.Add(new(fields[0], fields[3], fields[4], fields[5]));
        }
        return entries;
    }

    private static IReadOnlyDictionary<string, string> Names(IReadOnlyList<Entry> entries) => entries
        .Where(entry => !string.IsNullOrWhiteSpace(entry.English) && ResourceProjectTranslation.IsChinese(entry.Chinese))
        .GroupBy(entry => Normalize(entry.English)).ToDictionary(group => group.Key, group => group.First().Chinese);

    private static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
