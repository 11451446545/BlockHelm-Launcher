using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Updates;

if (args is ["--check-live"])
{
    var oldResult = await new Legacy91.RemoteManifestLauncherUpdateService().CheckForUpdatesAsync("26A17091", LauncherUpdateChannel.Release);
    Require(oldResult.Update?.Version == "26A17092" && oldResult.Update.CanAutoInstall, "Original 91 must discover the live patch.");
    foreach (var version in new[] { "26A17091-Compatible", "26A17092", "26A17092-Compatible", "26A17093" })
    {
        var result = await new RemoteManifestLauncherUpdateService().CheckForUpdatesAsync(version, LauncherUpdateChannel.Release);
        Require(!result.IsFailed && result.IsUpdateAvailable == (version == "26A17091-Compatible"), "Unexpected live update result: " + version);
    }
    Console.WriteLine("PASS: live manifest is accepted by original 91; compatibility, no-repeat and no-downgrade checks passed.");
    return;
}

Require(args.Length == 5, "Expected manifest, update EXE, original 91 EXE, modern payload EXE and isolated root.");
var manifest = Path.GetFullPath(args[0]);
var update = Path.GetFullPath(args[1]);
var baseline = Path.GetFullPath(args[2]);
var modernPayload = Path.GetFullPath(args[3]);
var root = Path.GetFullPath(args[4]);
Require(!Directory.Exists(root), "Test directory must be new.");
Directory.CreateDirectory(root);
var target = Path.Combine(root, "BlockHelm_Launcher_x64.exe");
File.Copy(baseline, target);
Require(FileVersionInfo.GetVersionInfo(target).ProductVersion == "26A17091", "Expected original 91 baseline.");
var data = Directory.CreateDirectory(Path.Combine(root, "BHL")).FullName;
var games = Directory.CreateDirectory(Path.Combine(root, "custom-games")).FullName;
var accounts = Directory.CreateDirectory(Path.Combine(root, "test-roaming", "BHL", "accounts")).FullName;
var gameSentinel = Path.Combine(games, "existing-world.bin");
var accountSentinel = Path.Combine(accounts, "existing-account.bin");
File.WriteAllText(gameSentinel, "preserve-world");
File.WriteAllText(accountSentinel, "preserve-account");
var settingsPath = Path.Combine(data, "settings.json");
File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
{
    HasAcceptedUserAgreement = true, Theme = "Dark", ThemeFollowSystem = false,
    MinecraftDirectory = games, EnableDiagnosticLogging = true
}));

using var client = new HttpClient(new LocalUpdateHandler(manifest, update));
var legacy = new Legacy91.RemoteManifestLauncherUpdateService(client);
var available = await legacy.CheckForUpdatesAsync("26A17091", LauncherUpdateChannel.Release);
Require(available.Update?.Version == "26A17092" && available.Update.CanAutoInstall, "Original 91 rejected patch metadata.");
Process? updater = null;
try
{
    var service = new Legacy91.LauncherSelfUpdateService(client, null, root, target, 0, startInfo =>
    {
        startInfo.WorkingDirectory = root;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        startInfo.Environment["AWP_DATA_DIRECTORY"] = Path.Combine(root, "test-roaming");
        startInfo.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "runtime-cache");
        updater = Process.Start(startInfo);
        return updater is not null;
    });
    var started = await service.StartUpdateAsync(available.Update!);
    Require(started.Succeeded && updater is not null, "Original 91 failed to download/verify/start the update.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    await updater!.WaitForExitAsync(timeout.Token);
    Require(updater.ExitCode == 0, "Update failed or rolled back; inspect isolated updater logs.");
    Require(FileVersionInfo.GetVersionInfo(target).ProductVersion == "26A17092", "Wrong installed version.");
    Require(Hash(target) == Hash(modernPayload), "Bootstrap did not install the exact modern payload.");
    Require(!File.Exists(target + ".update-pending.json") && !File.Exists(target + ".update-backup"), "Update transaction did not finish.");
    using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
    Require(settings.RootElement.GetProperty("Theme").GetString() == "Dark", "Selected theme was lost.");
    Require(settings.RootElement.GetProperty("MinecraftDirectory").GetString() == games, "Game directory was lost.");
    Require(File.ReadAllText(gameSentinel) == "preserve-world" && File.ReadAllText(accountSentinel) == "preserve-account", "Existing data changed.");
    var current = new RemoteManifestLauncherUpdateService(client);
    var latest = await current.CheckForUpdatesAsync("26A17092", LauncherUpdateChannel.Release);
    Require(!latest.IsFailed && !latest.IsUpdateAvailable, "Installed patch would update repeatedly.");
    Console.WriteLine("PASS: original 91 detected, downloaded, verified and applied patch; 92 confirmed startup; payload hash, settings, game/account data and no-repeat checks passed.");
}
finally
{
    if (updater is { HasExited: false }) updater.Kill(entireProcessTree: true);
    updater?.Dispose();
    foreach (var process in Process.GetProcessesByName("BlockHelm_Launcher_x64"))
    {
        using (process)
        {
            if (!string.Equals(process.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) continue;
            process.CloseMainWindow();
            if (!process.WaitForExit(10_000)) process.Kill();
        }
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

sealed class LocalUpdateHandler(string manifest, string executable) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal) == true ? manifest : executable;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StreamContent(File.OpenRead(path))
        };
        response.Content.Headers.ContentLength = new FileInfo(path).Length;
        return Task.FromResult(response);
    }
}
