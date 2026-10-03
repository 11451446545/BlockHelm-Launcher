using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Updates;
using Xunit;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class ReleaseIdentityUpdateTests
{
    private static readonly LauncherReleaseIdentity Current = new("26A17094-Compatible", "26A17094", 648114324);

    [Theory]
    [InlineData("春季特别版", "full")]
    [InlineData("维护补丁 · 第二次修复", "patch")]
    [InlineData("0.1.0-new-name", "full")]
    public async Task DisplayNameDoesNotControlOrderingAndMatchingPatchCanInstall(string name, string kind)
    {
        var result = await Check(Manifest(name, kind));
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(name, result.Update!.DisplayVersion);
        Assert.Equal("release-spring", result.Update.ReleaseId);
        Assert.True(result.Update.CanAutoInstall);
        Assert.Equal(kind == "patch" ? LauncherReleaseKind.Patch : LauncherReleaseKind.Full, result.Update.ReleaseKind);
        Assert.Equal("新版本简介", result.Update.Summary);
        Assert.Equal("修复下载和显示", result.Update.Changelog);
    }

    [Theory]
    [InlineData("26A17094", 648114325)]
    [InlineData("older-release", 648114323)]
    [InlineData("renamed-release", 648114324)]
    public async Task SameIdentityOrOlderSequenceNeverRepeatsOrDowngrades(string id, long sequence)
    {
        var manifest = Manifest("99999999-最大名称", "full");
        manifest["releaseId"] = id;
        manifest["releaseSequence"] = sequence;
        Assert.False((await Check(manifest)).IsUpdateAvailable);
    }

    [Fact]
    public async Task CustomInstalledNameUsesEmbeddedIdentityWithoutVersionParsing()
    {
        var result = await Check(Manifest("新春版", "full"), new("我的专用启动器", "26A17094", 648114324));
        Assert.True(result.Update!.CanAutoInstall);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongBaseOrInstallerDeliveryCannotEnterSelfReplacement(bool installer)
    {
        var manifest = Manifest("维护补丁", "patch");
        if (installer) manifest["assets"]![0]!["delivery"] = "installer";
        else manifest["baseReleaseIds"] = new JsonArray("another-base");
        var result = await Check(manifest);
        Assert.True(result.IsUpdateAvailable);
        Assert.False(result.Update!.CanAutoInstall);
        Assert.Equal(installer, result.Update.IsApplicable);
    }

    [Theory]
    [InlineData("releaseId", "../outside")]
    [InlineData("releaseType", "unknown")]
    [InlineData("releaseSequence", "invalid")]
    public async Task InvalidIdentityCannotBecomeInstallable(string field, string value)
    {
        var manifest = Manifest("命名无关", "patch");
        manifest[field] = value;
        Assert.True((await Check(manifest)).IsFailed);
    }

    [Fact]
    public async Task PatchMustDeclareItsBaseAndDeliveryMustBeExplicit()
    {
        var manifest = Manifest("补丁", "patch");
        manifest.Remove("baseReleaseIds");
        Assert.True((await Check(manifest)).IsFailed);
        manifest = Manifest("完整版本", "full");
        manifest["assets"]![0]!.AsObject().Remove("delivery");
        Assert.True((await Check(manifest)).IsFailed);
    }

    private static JsonObject Manifest(string name, string kind) => new()
    {
        ["schemaVersion"] = 2, ["appId"] = "BlockHelm-Launcher", ["channel"] = "release",
        ["versionName"] = name, ["releaseId"] = "release-spring", ["releaseSequence"] = 648114325L,
        ["releaseType"] = kind, ["baseReleaseIds"] = new JsonArray("26A17094"),
        ["summary"] = "新版本简介", ["releaseNotes"] = "修复下载和显示",
        ["assets"] = new JsonArray(new JsonObject
        {
            ["platform"] = "windows", ["arch"] = "x64", ["packageType"] = "exe", ["delivery"] = "self-update",
            ["fileName"] = "update.exe", ["size"] = 123, ["sha256"] = new string('a', 64),
            ["urls"] = new JsonArray(new JsonObject { ["name"] = "github", ["priority"] = 1,
                ["url"] = "https://github.com/11451446545/BlockHelm-Launcher/releases/download/test/update.exe" })
        })
    };
    private static Task<LauncherUpdateCheckResult> Check(JsonObject manifest, LauncherReleaseIdentity? identity = null)
    {
        var client = new HttpClient(new Handler(manifest.ToJsonString()));
        return new RemoteManifestLauncherUpdateService(client).CheckForUpdatesAsync(identity ?? Current, LauncherUpdateChannel.Release);
    }
    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
