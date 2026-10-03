namespace Launcher.Domain.Models;

/// <summary>Display text only. Project identifiers, versions and filenames are never translated.</summary>
public sealed record ResourceProjectTranslation(string? Title = null, string? Description = null)
{
    public static bool IsChinese(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 16000
        && value.Any(c => c is >= '\u3400' and <= '\u9fff');
}
