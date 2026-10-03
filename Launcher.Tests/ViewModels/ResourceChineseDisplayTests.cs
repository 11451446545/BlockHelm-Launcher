using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Xunit;

namespace Launcher.Tests.ViewModels;

public sealed class ResourceChineseDisplayTests
{
    [Theory]
    [InlineData(ResourceProjectKind.Mod)]
    [InlineData(ResourceProjectKind.ResourcePack)]
    [InlineData(ResourceProjectKind.Modpack)]
    [InlineData(ResourceProjectKind.ShaderPack)]
    [InlineData(ResourceProjectKind.World)]
    public async Task FailedNameTranslationFallsBackToOriginalAndSuccessfulRetryRestoresChinese(ResourceProjectKind kind)
    {
        var localizer = new DeferredLocalizer();
        var item = new ResourcesModProjectItemViewModel(new ResourceProject { Kind = kind, Title = "New resource", Description = "English summary" },
            localizer: localizer, dispatcher: ImmediateUiDispatcher.Instance);
        Assert.Equal(Strings.Resources_ChineseTitleLoading, item.Title);
        Assert.Equal(Strings.Resources_ChineseDescriptionLoading, item.Description);
        var failed = Changed(item);
        localizer.Completion.SetResult(new());
        await failed;
        Assert.Equal("New resource", item.Title);
        Assert.Equal(Strings.Resources_ChineseDescriptionUnavailable, item.Description);
        Assert.True(item.CanRetryTranslation);
        localizer.Completion = new();
        var retry = item.RetryTranslationCommand.ExecuteAsync(null);
        localizer.Completion.SetResult(new("新资源", "中文简介"));
        await retry;
        Assert.Equal("新资源", item.Title);
        Assert.Equal("中文简介", item.Description);
        Assert.False(item.CanRetryTranslation);
    }

    [Fact]
    public void DependenciesFallBackToOriginalNamesAndVersionsPreferChineseProjectNames()
    {
        var project = new ResourceProject { Title = "Original English Name" };
        var dependency = new ResourcesModDependencyRequirementItemViewModel(new ResourceProjectDependency { Project = project }, null, null,
            ResourceDependencyRequirementState.Missing);
        Assert.Equal(project.Title, dependency.Title);
        var version = new ResourcesModVersionItemViewModel(new ResourceProjectVersion { Name = "English release name", VersionNumber = "1.2.3", FileName = "actual.jar" },
            new ResourcesModProjectItemViewModel(new ResourceProject { Title = "中文资源" }));
        Assert.Equal("中文资源 · 1.2.3", version.Title);
        Assert.Contains("actual.jar", version.Subtitle);
    }

    private static Task Changed(ResourceProjectTextViewModel item)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        item.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(item.CanRetryTranslation) && item.CanRetryTranslation) completion.TrySetResult(); };
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private sealed class DeferredLocalizer : IResourceProjectLocalizer
    {
        public TaskCompletionSource<ResourceProjectTranslation> Completion { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ResourceProjectTranslation> LocalizeAsync(ResourceProject project, CancellationToken cancellationToken = default) => Completion.Task;
    }
}
