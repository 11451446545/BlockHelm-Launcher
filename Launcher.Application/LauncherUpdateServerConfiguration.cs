namespace Launcher.Application;

public sealed record LauncherUpdateServerConfiguration(
    string ReleaseManifestUrlTemplate,
    string PreviewManifestUrlTemplate)
{
    public static LauncherUpdateServerConfiguration Default { get; } = new(
        LauncherProjectLinks.GitHubUpdateManifestUrlTemplate,
        LauncherProjectLinks.GitHubUpdateManifestUrlTemplate);

    public string CreateManifestUrl(string channel) => string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        channel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? PreviewManifestUrlTemplate
            : ReleaseManifestUrlTemplate,
        channel);
}
