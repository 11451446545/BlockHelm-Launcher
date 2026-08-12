using System.Net;
using System.Text;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class RemoteManifestLauncherUpdateServiceTests
{
    private const string Sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string GitHubManifest = "https://raw.githubusercontent.com/11451446545/BlockHelm-Launcher/update-manifests/update/release/latest.json";

    [Fact]
    public async Task ValidManifestIsAcceptedWithoutSignatureSidecar()
    {
        var service = CreateService((GitHubManifest, HttpStatusCode.OK, CreateManifest()));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
        Assert.True(result.Update?.CanAutoInstall);
        Assert.Equal(12, result.Update?.SizeBytes);
        Assert.Equal(Sha256, result.Update?.Sha256);
    }

    [Theory]
    [InlineData(LauncherUpdateChannel.Release, "release")]
    [InlineData(LauncherUpdateChannel.Beta, "beta")]
    public async Task NextHexadecimalManifestIsAvailableFromEitherChannel(
        LauncherUpdateChannel channel,
        string channelName)
    {
        const string nextVersion = "26A17090";
        const int nextVersionCode = 648114320;
        var manifestUrl = GitHubManifest.Replace(
            "/release/latest.json",
            $"/{channelName}/latest.json",
            StringComparison.Ordinal);
        var service = CreateService((
            manifestUrl,
            HttpStatusCode.OK,
            CreateManifest(
                version: nextVersion,
                versionCode: nextVersionCode,
                channel: channelName,
                downloadUrl: "https://github.com/11451446545/BlockHelm-Launcher/releases/download/v26A17090/BlockHelm_Launcher_x64.exe")));

        var result = await service.CheckForUpdatesAsync("26A1708F", channel);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(nextVersion, result.Update?.Version);
        Assert.Equal(nextVersionCode, result.Update?.VersionCode);
        Assert.True(result.Update?.CanAutoInstall);
    }

    [Fact]
    public async Task HexadecimalBetaManifestAdvancesFromCurrentStableBuild()
    {
        const string nextVersion = "26A17091";
        const int nextVersionCode = 648114321;
        var betaManifest = GitHubManifest.Replace(
            "/release/latest.json",
            "/beta/latest.json",
            StringComparison.Ordinal);
        var service = CreateService((
            betaManifest,
            HttpStatusCode.OK,
            CreateManifest(
                version: nextVersion,
                versionCode: nextVersionCode,
                channel: "beta",
                downloadUrl: "https://github.com/11451446545/BlockHelm-Launcher/releases/download/v26A17091-beta.1/BlockHelm_Launcher_x64.exe")));

        var result = await service.CheckForUpdatesAsync("26A17090", LauncherUpdateChannel.Beta);

        Assert.False(result.IsFailed);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(nextVersion, result.Update?.Version);
        Assert.Equal(nextVersionCode, result.Update?.VersionCode);
        Assert.True(result.Update?.CanAutoInstall);
    }

    [Theory]
    [InlineData(0, Sha256)]
    [InlineData(12, "abcd")]
    public async Task MissingRequiredExecutableIntegrityMetadataIsRejected(long size, string sha256)
    {
        var service = CreateService((GitHubManifest, HttpStatusCode.OK, CreateManifest(size: size, sha256: sha256)));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OversizedExecutableIsRejectedBeforeDownload()
    {
        var service = CreateService((
            GitHubManifest,
            HttpStatusCode.OK,
            CreateManifest(size: 512L * 1024 * 1024 + 1)));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task VersionNameAndVersionCodeMustMatch()
    {
        var service = CreateService((GitHubManifest, HttpStatusCode.OK, CreateManifest(version: "1.1.1")));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task NullAssetsCollectionIsRejected()
    {
        const string manifest = """
        {
          "schemaVersion": 1,
          "appId": "BlockHelm-Launcher",
          "channel": "release",
          "versionName": "1.1.0",
          "versionCode": 1010099,
          "assets": null
        }
        """;
        var service = CreateService((GitHubManifest, HttpStatusCode.OK, manifest));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task NullDownloadUrlCollectionIsRejected()
    {
        const string manifest = """
        {
          "schemaVersion": 1,
          "appId": "BlockHelm-Launcher",
          "channel": "release",
          "versionName": "1.1.0",
          "versionCode": 1010099,
          "assets": [{
            "platform": "windows",
            "arch": "x64",
            "packageType": "exe",
            "fileName": "BlockHelm_Launcher_x64.exe",
            "size": 12,
            "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "urls": null
          }]
        }
        """;
        var service = CreateService((GitHubManifest, HttpStatusCode.OK, manifest));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    private static IReadOnlyList<LauncherUpdateManifestSource> DefaultSources =>
    [
        new("github", GitHubManifest.Replace("/release/latest.json", "/{0}/latest.json"), 1)
    ];

    [Fact]
    public void DefaultSourcesUseOnlyTheConfiguredGitHubRepository()
    {
        Assert.Single(LauncherUpdateManifestSource.DefaultSources);
        var source = LauncherUpdateManifestSource.DefaultSources[0];
        Assert.Equal("github", source.Name);
        Assert.Contains("raw.githubusercontent.com/11451446545/BlockHelm-Launcher/", source.UrlTemplate);
    }

    [Fact]
    public async Task ManifestFromAnotherGitHubRepositoryIsRejected()
    {
        const string untrustedManifest = "https://raw.githubusercontent.com/other-owner/other-repo/update-manifests/update/release/latest.json";
        var service = CreateService(
            [new LauncherUpdateManifestSource("github", untrustedManifest, 1)],
            (untrustedManifest, HttpStatusCode.OK, CreateManifest()));

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ManifestRedirectToAnUntrustedHttpsHostIsRejected()
    {
        const string untrustedManifest = "https://example.test/latest.json";
        var handler = new RedirectHandler(GitHubManifest, untrustedManifest);
        var service = new RemoteManifestLauncherUpdateService(
            new HttpClient(handler),
            null,
            DefaultSources);

        var result = await service.CheckForUpdatesAsync("1.0.0", LauncherUpdateChannel.Release);

        Assert.True(result.IsFailed);
        Assert.Equal([GitHubManifest], handler.Requests);
    }

    private static RemoteManifestLauncherUpdateService CreateService(
        params (string Url, HttpStatusCode Status, string Content)[] responses) =>
        CreateService([DefaultSources[0]], responses);

    private static RemoteManifestLauncherUpdateService CreateService(
        IReadOnlyList<LauncherUpdateManifestSource> sources,
        params (string Url, HttpStatusCode Status, string Content)[] responses)
    {
        var handler = new ResponseHandler(responses);
        return new RemoteManifestLauncherUpdateService(new HttpClient(handler), null, sources);
    }

    private static string CreateManifest(
        string version = "1.1.0",
        int versionCode = 1010099,
        string channel = "release",
        long size = 12,
        string sha256 = Sha256,
        string downloadUrl = "https://github.com/11451446545/BlockHelm-Launcher/releases/download/v1.1.0/BlockHelm_Launcher_x64.exe") => $$"""
    {
      "schemaVersion": 1,
      "appId": "BlockHelm-Launcher",
      "channel": "{{channel}}",
      "versionName": "{{version}}",
      "versionCode": {{versionCode}},
      "publishedAt": "2026-07-12T00:00:00Z",
      "mandatory": false,
      "minSupportedVersionCode": 0,
      "releaseNotes": "test",
      "assets": [{
        "platform": "windows", "arch": "x64", "packageType": "exe",
        "fileName": "BlockHelm_Launcher_x64.exe", "size": {{size}}, "sha256": "{{sha256}}",
        "urls": [{ "name": "github", "url": "{{downloadUrl}}", "priority": 1 }]
      }]
    }
    """;

    private sealed class ResponseHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Content)> responses;

        public ResponseHandler(IEnumerable<(string Url, HttpStatusCode Status, string Content)> values) =>
            responses = values.ToDictionary(
                value => value.Url,
                value => (value.Status, value.Content),
                StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var value = responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var configured)
                ? configured
                : (Status: HttpStatusCode.NotFound, Content: string.Empty);
            return Task.FromResult(new HttpResponseMessage(value.Status)
            {
                RequestMessage = request,
                Content = new StringContent(value.Content, Encoding.UTF8)
            });
        }
    }

    private sealed class RedirectHandler(string sourceUrl, string redirectUrl) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request
            };
            if (string.Equals(url, sourceUrl, StringComparison.OrdinalIgnoreCase))
                response.Headers.Location = new Uri(redirectUrl);
            return Task.FromResult(response);
        }
    }
}
