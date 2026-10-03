using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IResourceProjectLocalizer
{
    Task<ResourceProjectTranslation> LocalizeAsync(ResourceProject project, CancellationToken cancellationToken = default);
}
