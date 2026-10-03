using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Resources;
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
    public void MissingDictionaryEntryDisplaysOriginalNameAndDescription(ResourceProjectKind kind)
    {
        var item = new ResourcesModProjectItemViewModel(new ResourceProject { Kind = kind, Title = "New resource", Description = "English summary" },
            localizer: new ResourceDictionaryLocalizer(), dispatcher: ImmediateUiDispatcher.Instance);
        Assert.Equal("New resource", item.Title);
        Assert.Equal("English summary", item.Description);
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

}
