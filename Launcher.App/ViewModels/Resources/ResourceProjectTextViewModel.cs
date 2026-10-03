using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

/// <summary>Prefers Chinese translations and preserves the original name when translation is unavailable.</summary>
public abstract partial class ResourceProjectTextViewModel : ObservableObject
{
    private readonly IResourceProjectLocalizer? localizer;
    private readonly IUiDispatcher dispatcher;
    private ResourceProjectTranslation translation = new();
    private bool loading;

    protected ResourceProjectTextViewModel(ResourceProject project, IResourceProjectLocalizer? localizer, IUiDispatcher? dispatcher)
    {
        Project = project;
        this.localizer = localizer;
        this.dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        loading = localizer is not null;
        _ = RefreshTranslationAsync();
    }

    public ResourceProject Project { get; }
    internal IResourceProjectLocalizer? Localizer => localizer;
    internal IUiDispatcher Dispatcher => dispatcher;
    public virtual string Title => Chinese(translation.Title) ?? Chinese(Project.Title)
        ?? (loading ? Strings.Resources_ChineseTitleLoading
            : !string.IsNullOrWhiteSpace(Project.Title) ? Project.Title : Strings.Resources_ChineseTitleUnavailable);
    public string Description => Chinese(translation.Description) ?? Chinese(Project.Description)
        ?? (string.IsNullOrWhiteSpace(Project.Description) ? Strings.Resources_ChineseDescriptionEmpty
            : loading ? Strings.Resources_ChineseDescriptionLoading : Strings.Resources_ChineseDescriptionUnavailable);
    public bool CanRetryTranslation => !loading && localizer is not null
        && (Chinese(translation.Title) is null && Chinese(Project.Title) is null
            || !string.IsNullOrWhiteSpace(Project.Description) && Chinese(translation.Description) is null && Chinese(Project.Description) is null);

    private static string? Chinese(string? value) => ResourceProjectTranslation.IsChinese(value) ? value : null;

    [RelayCommand]
    private async Task RetryTranslationAsync() => await RefreshTranslationAsync();

    private async Task RefreshTranslationAsync()
    {
        if (localizer is null) return;
        dispatcher.Invoke(() => { loading = true; NotifyText(); });
        ResourceProjectTranslation result;
        try { result = await localizer.LocalizeAsync(Project).ConfigureAwait(false); }
        catch (Exception) { result = new(); } // Provider failures must never become unobserved UI tasks.
        dispatcher.Invoke(() => { translation = result; loading = false; NotifyText(); });
    }

    private void NotifyText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CanRetryTranslation));
    }
}
