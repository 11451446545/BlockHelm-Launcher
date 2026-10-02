using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Settings;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class ThemeSettingsViewModelTests : TestTempDirectory
{
    [Theory]
    [InlineData("Dark", EffectiveTheme.Dark)]
    [InlineData("Light", EffectiveTheme.Light)]
    public async Task ManualThemeSelectionAppliesAndSurvivesSystemToggleAndReload(
        string requestedTheme,
        EffectiveTheme expectedTheme)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                VerifySelection(requestedTheme, expectedTheme);
                completion.TrySetResult(true);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private void VerifySelection(string requestedTheme, EffectiveTheme expectedTheme)
    {
        var settingsService = new JsonSettingsService(TempRoot);
        var settings = new LauncherSettings
        {
            Theme = requestedTheme == "Dark" ? "Light" : "Dark",
            ThemeFollowSystem = false
        };
        using var persistence = new SettingsPersistenceCoordinator(
            settingsService, new StatusService(), NullLogger.Instance);
        persistence.Prime(settings);
        using var themeService = new ThemeService(ImmediateUiDispatcher.Instance);
        themeService.ApplyPreference(settings.Theme, false, 0);
        var viewModel = new ThemeSettingsViewModel(persistence, themeService, null);
        viewModel.Load(settings);

        // Choose the labelled option shown to the user, not a hard-coded ID.
        var title = requestedTheme == "Dark"
            ? Strings.Settings_ThemeDarkTitle
            : Strings.Settings_ThemeLightTitle;
        viewModel.SelectedThemeOption = Assert.Single(viewModel.ThemeOptions, option => option.Title == title);
        Assert.Equal(expectedTheme, themeService.EffectiveTheme);
        Assert.True(viewModel.IsThemeSelectionVisible);

        viewModel.FollowSystemTheme = true;
        Assert.False(viewModel.IsThemeSelectionVisible);
        viewModel.FollowSystemTheme = false;
        Assert.Equal(expectedTheme, themeService.EffectiveTheme);
        persistence.FlushAsync().GetAwaiter().GetResult();

        // A fresh persistence service exercises the same JSON normalization used at startup.
        var restored = new JsonSettingsService(TempRoot).LoadAsync().GetAwaiter().GetResult();
        Assert.Equal(requestedTheme, restored.Theme);
        Assert.False(restored.ThemeFollowSystem);
        viewModel.Load(restored);
        Assert.Equal(title, viewModel.SelectedThemeOption?.Title);
        themeService.ApplyPreference(restored.Theme, restored.ThemeFollowSystem, 0);
        Assert.Equal(expectedTheme, themeService.EffectiveTheme);
    }
}
