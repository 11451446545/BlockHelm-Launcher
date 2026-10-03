using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;

if (args is ["--check-live"]) {
    var service = new Shipped94.RemoteManifestLauncherUpdateService();
    foreach (var version in new[] { "26A17094", "26A17094-Compatible" }) {
        var result = await service.CheckForUpdatesAsync(new LauncherReleaseIdentity(version, "26A17094", 648114324), LauncherUpdateChannel.Release);
        Require(result.Update is { CanAutoInstall: true, ReleaseKind: LauncherReleaseKind.Patch, ReleaseId: "26A17094-patch.1" },
            "Shipped 94 did not discover the live patch: " + version);
    }
    var latest = await service.CheckForUpdatesAsync(new LauncherReleaseIdentity("26A17094-Patch1", "26A17094-patch.1", 648114325), LauncherUpdateChannel.Release);
    Require(!latest.IsFailed && !latest.IsUpdateAvailable, "Live patch would repeat.");
    Console.WriteLine("PASS: shipped 94 modern/compatible clients accept the live patch; patched identity does not repeat.");
    return;
}

Require(args.Length == 5, "Expected manifest, update EXE, shipped 94 EXE, payload EXE and isolated root.");
var manifest = Path.GetFullPath(args[0]);
var package = Path.GetFullPath(args[1]);
var baseline = Path.GetFullPath(args[2]);
var payload = Path.GetFullPath(args[3]);
var root = Path.GetFullPath(args[4]);
var baselineVersion = FileVersionInfo.GetVersionInfo(baseline).ProductVersion!;
Require(baselineVersion is "26A17094" or "26A17094-Compatible", "Use a shipped 94 baseline.");
Require(!Directory.Exists(root), "Test directory must be new.");
Directory.CreateDirectory(root);
var target = Path.Combine(root, "BlockHelm_Launcher_x64.exe");
File.Copy(baseline, target);
var data = Directory.CreateDirectory(Path.Combine(root, "BHL")).FullName;
var games = Directory.CreateDirectory(Path.Combine(root, "custom-games")).FullName;
var accounts = Directory.CreateDirectory(Path.Combine(root, "test-roaming", "BHL", "accounts")).FullName;
var gameSentinel = Path.Combine(games, "existing-world.bin");
var accountSentinel = Path.Combine(accounts, "existing-account.bin");
File.WriteAllText(gameSentinel, "preserve-world");
File.WriteAllText(accountSentinel, "preserve-account");
var settingsPath = Path.Combine(data, "settings.json");
File.WriteAllText(settingsPath, JsonSerializer.Serialize(new {
    HasAcceptedUserAgreement = true, Theme = "Dark", ThemeFollowSystem = false,
    MinecraftDirectory = games, EnableDiagnosticLogging = true
}));
using var client = new HttpClient(new LocalHandler(manifest, package));
var shipped = new Shipped94.RemoteManifestLauncherUpdateService(client);
var available = await shipped.CheckForUpdatesAsync(new LauncherReleaseIdentity(baselineVersion, "26A17094", 648114324), LauncherUpdateChannel.Release);
Require(available.Update is { CanAutoInstall: true, IsApplicable: true, ReleaseKind: LauncherReleaseKind.Patch, ReleaseId: "26A17094-patch.1" },
    "Shipped 94 must discover an applicable patch.");
Require(available.Update!.Summary?.Contains("模糊过渡") == true, "Patch summary must be visible.");
var wrongBase = await shipped.CheckForUpdatesAsync(new LauncherReleaseIdentity("26A17093", "26A17093", 648114323), LauncherUpdateChannel.Release);
Require(wrongBase.Update is { CanAutoInstall: false, IsApplicable: false }, "Other base releases must not self-install this patch.");
Process? updater = null;
try {
    var selfUpdate = new Shipped94.LauncherSelfUpdateService(client, null, root, target, 0, info => {
        info.WorkingDirectory = root;
        info.WindowStyle = ProcessWindowStyle.Hidden;
        info.Environment["AWP_DATA_DIRECTORY"] = Path.Combine(root, "test-roaming");
        info.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "runtime-cache");
        updater = Process.Start(info);
        return updater is not null;
    });
    var result = await selfUpdate.StartUpdateAsync(available.Update!);
    Require(result.Succeeded && updater is not null, "Shipped 94 failed to download, verify or start the patch.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    await updater!.WaitForExitAsync(timeout.Token);
    Require(updater.ExitCode == 0, "Update failed or rolled back.");
    Require(Hash(target) == Hash(payload), "Installed payload differs from tested build.");
    Require(FileVersionInfo.GetVersionInfo(target).ProductVersion == "26A17094-Patch1", "Wrong installed patch version.");
    Require(!File.Exists(target + ".update-pending.json") && !File.Exists(target + ".update-backup"), "Update transaction unfinished.");
    using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
    Require(settings.RootElement.GetProperty("Theme").GetString() == "Dark", "Theme lost.");
    Require(settings.RootElement.GetProperty("MinecraftDirectory").GetString() == games, "Game path lost.");
    Require(File.ReadAllText(gameSentinel) == "preserve-world" && File.ReadAllText(accountSentinel) == "preserve-account", "Existing data changed.");
    var latest = await shipped.CheckForUpdatesAsync(new LauncherReleaseIdentity("26A17094-Patch1", "26A17094-patch.1", 648114325), LauncherUpdateChannel.Release);
    Require(!latest.IsFailed && !latest.IsUpdateAvailable, "Installed patch would update repeatedly.");
    Console.WriteLine($"PASS: shipped {baselineVersion} detected, downloaded, verified and applied Patch1; startup confirmed; exact payload, game/account/settings preservation, base restriction and no-repeat checks passed.");
} finally {
    if (updater is { HasExited: false }) updater.Kill(entireProcessTree: true);
    updater?.Dispose();
    foreach (var process in Process.GetProcessesByName("BlockHelm_Launcher_x64")) {
        using (process) {
            if (!string.Equals(process.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) continue;
            process.CloseMainWindow();
            if (!process.WaitForExit(10000)) process.Kill();
        }
    }
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
sealed class LocalHandler(string manifest, string package) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        var file = request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal) ? manifest : package;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(File.OpenRead(file)) };
        response.Content.Headers.ContentLength = new FileInfo(file).Length;
        return Task.FromResult(response);
    }
}
