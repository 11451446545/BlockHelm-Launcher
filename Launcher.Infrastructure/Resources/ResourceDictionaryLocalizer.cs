using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Resources;

/// <summary>Uses bundled community names only; never requests translations or reads machine translation caches.</summary>
public sealed class ResourceDictionaryLocalizer : IResourceProjectLocalizer
{
    public Task<ResourceProjectTranslation> LocalizeAsync(ResourceProject project, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var title = ResourceProjectTranslation.IsChinese(project.Title) ? project.Title : ResourceSearchIndex.ChineseTitle(project);
        return Task.FromResult(new ResourceProjectTranslation(title, project.Description));
    }
}
