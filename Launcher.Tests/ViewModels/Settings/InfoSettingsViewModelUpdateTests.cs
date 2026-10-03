using Launcher.App.Services;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class InfoSettingsViewModelUpdateTests
{
    [Fact]
    public async Task DialogDisplaysPublisherSummaryAndFullNotesAndResetsMissingNotes()
    {
        var update = CreateUpdate() with { Summary = "资源中心中文版", Changelog = "- 中文名称\n- 更新识别修复", ReleaseKind = LauncherReleaseKind.Patch };
        using var context = CreateContext(LauncherUpdateCheckResult.Available("26A17091", update));
        await context.ViewModel.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(update.Summary, context.ViewModel.UpdateDialogSummary);
        Assert.Equal(update.Changelog, context.ViewModel.UpdateDialogChangelog);
        Assert.Equal(Strings.Dialog_UpdateKindPatch, context.ViewModel.UpdateDialogKindText);
        context.ViewModel.CancelUpdateDialogCommand.Execute(null);
        context.UpdateService.Result = LauncherUpdateCheckResult.Available("26A17091", CreateUpdate());
        await context.ViewModel.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(Strings.Dialog_UpdateSummaryUnavailable, context.ViewModel.UpdateDialogSummary);
        Assert.Equal(Strings.Dialog_UpdateChangelogUnavailable, context.ViewModel.UpdateDialogChangelog);
    }

    [Fact]
    public async Task LegacyNotesSupplySummaryAndIncompatiblePatchNeverStartsUpdater()
    {
        var update = CreateUpdate() with { Changelog = "# 维护更新\n\n- 修复下载", ReleaseKind = LauncherReleaseKind.Patch, IsApplicable = false };
        using var context = CreateContext(LauncherUpdateCheckResult.Available("26A17091", update));
        await context.ViewModel.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.Equal("维护更新", context.ViewModel.UpdateDialogSummary);
        Assert.Equal(Strings.Dialog_UpdatePatchNotApplicable, context.ViewModel.UpdateDialogInstallHint);
        Assert.Equal(Strings.Dialog_UpdateDownloadButton, context.ViewModel.ConfirmUpdateButtonText);
        await context.ViewModel.ConfirmUpdateCommand.ExecuteAsync(null);
        Assert.Null(context.SelfUpdateService.Update);
        Assert.Equal(0, context.ExitService.ShutdownCount);
    }

    private const string DownloadUrl =
        "https://github.com/11451446545/BlockHelm-Launcher/releases/download/v1.0.1/BlockHelm_Launcher_x64.exe";

    [Fact]
    public async Task ManualCheckUsesTheGitHubManifestService()
    {
        using var context = CreateContext(LauncherUpdateCheckResult.Latest("0.9.13"));

        await context.ViewModel.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(1, context.UpdateService.CallCount);
        Assert.Contains(Strings.Status_LauncherAlreadyLatest, context.Status.Messages);
        Assert.False(context.ViewModel.IsCheckingUpdates);
    }

    [Fact]
    public async Task StartupCheckUsesTheSameManifestServiceAndShowsAnUpdate()
    {
        var update = CreateUpdate();
        using var context = CreateContext(LauncherUpdateCheckResult.Available("0.9.13", update));

        await context.ViewModel.CheckUpdatesOnStartupAsync();

        Assert.Equal(1, context.UpdateService.CallCount);
        Assert.True(context.ViewModel.IsUpdateAvailableDialogOpen);
        Assert.Equal(update.DisplayVersion, context.ViewModel.UpdateDialogVersionText);
    }

    [Fact]
    public async Task ConfirmingAnUpdateStartsTheSelfUpdaterAndShutsDown()
    {
        var update = CreateUpdate();
        using var context = CreateContext(
            LauncherUpdateCheckResult.Available("0.9.13", update),
            LauncherSelfUpdateStartResult.Success("C:\\temp\\update.exe"));
        await context.ViewModel.CheckUpdatesOnStartupAsync();

        await context.ViewModel.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Same(update, context.SelfUpdateService.Update);
        Assert.Equal(1, context.ExitService.ShutdownCount);
        Assert.False(context.ViewModel.IsUpdateAvailableDialogOpen);
    }

    [Fact]
    public async Task FailedSelfUpdateDoesNotShutDownOrCloseTheDialog()
    {
        using var context = CreateContext(
            LauncherUpdateCheckResult.Available("0.9.13", CreateUpdate()),
            LauncherSelfUpdateStartResult.Failed("download failed"));
        await context.ViewModel.CheckUpdatesOnStartupAsync();

        await context.ViewModel.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, context.ExitService.ShutdownCount);
        Assert.True(context.ViewModel.IsUpdateAvailableDialogOpen);
        Assert.Contains(Strings.Status_LauncherUpdateStartFailed, context.Status.Messages);
    }

    private static TestContext CreateContext(
        LauncherUpdateCheckResult checkResult,
        LauncherSelfUpdateStartResult? startResult = null)
    {
        var settings = new LauncherSettings();
        var status = new RecordingStatusService();
        var settingsService = new TestSettingsService(settings);
        var persistence = new SettingsPersistenceCoordinator(settingsService, status, NullLogger.Instance);
        persistence.Prime(settings);
        var updateService = new RecordingUpdateService(checkResult);
        var selfUpdateService = new RecordingSelfUpdateService(
            startResult ?? LauncherSelfUpdateStartResult.Failed("not configured"));
        var exitService = new RecordingExitService();
        var viewModel = new InfoSettingsViewModel(
            persistence,
            status,
            new RecordingFloatingMessageService(),
            new AlwaysOpenExternalLinkService(),
            updateService,
            selfUpdateService,
            exitService,
            new EmptyReferenceProjectCatalog(),
            NullLogger<InfoSettingsViewModel>.Instance);
        return new TestContext(viewModel, updateService, selfUpdateService, exitService, status, persistence);
    }

    private static LauncherUpdateInfo CreateUpdate()
    {
        return new LauncherUpdateInfo(
            "1.0.1",
            "1.0.1",
            "https://github.com/11451446545/BlockHelm-Launcher/releases/tag/v1.0.1",
            DownloadUrl,
            null,
            "BlockHelm_Launcher_x64.exe",
            LauncherUpdateAssetKind.WindowsX64Executable,
            12,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            VersionCode: 1000199,
            DownloadUrls: [new LauncherUpdateDownloadUrl("github", DownloadUrl, 1)]);
    }

    private sealed record TestContext(
        InfoSettingsViewModel ViewModel,
        RecordingUpdateService UpdateService,
        RecordingSelfUpdateService SelfUpdateService,
        RecordingExitService ExitService,
        RecordingStatusService Status,
        SettingsPersistenceCoordinator Persistence) : IDisposable
    {
        public void Dispose()
        {
            Persistence.Dispose();
        }
    }

    private sealed class RecordingUpdateService(LauncherUpdateCheckResult result) : ILauncherUpdateService
    {
        public LauncherUpdateCheckResult Result { get; set; } = result;
        public int CallCount { get; private set; }

        public Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
            string currentVersion,
            LauncherUpdateChannel channel,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingSelfUpdateService(LauncherSelfUpdateStartResult result) : ILauncherSelfUpdateService
    {
        public LauncherUpdateInfo? Update { get; private set; }

        public Task<LauncherSelfUpdateStartResult> StartUpdateAsync(
            LauncherUpdateInfo update,
            CancellationToken cancellationToken = default)
        {
            Update = update;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingExitService : IApplicationExitService
    {
        public int ShutdownCount { get; private set; }

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }

    private sealed class RecordingStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
            MessageReported?.Invoke(message);
        }
    }

    private sealed class RecordingFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public void Show(string message)
        {
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class AlwaysOpenExternalLinkService : IExternalLinkService
    {
        public bool TryOpen(string url) => true;
    }

    private sealed class EmptyReferenceProjectCatalog : IInfoReferenceProjectCatalog
    {
        public IReadOnlyList<InfoReferenceProjectItem> GetProjects() => [];
    }
}
