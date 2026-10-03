using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

/// <summary>Prefers community Chinese names and displays original text when no entry is available.</summary>
public abstract partial class ResourceProjectTextViewModel : ObservableObject
{
    private readonly IResourceProjectLocalizer? localizer;
    private readonly IUiDispatcher dispatcher;
    private ResourceProjectTranslation translation = new();

    protected ResourceProjectTextViewModel(ResourceProject project, IResourceProjectLocalizer? localizer, IUiDispatcher? dispatcher)
    {
        Project = project;
        this.localizer = localizer;
        this.dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _ = RefreshTranslationAsync();
    }

    public ResourceProject Project { get; }
    internal IResourceProjectLocalizer? Localizer => localizer;
    internal IUiDispatcher Dispatcher => dispatcher;
    public virtual string Title => Chinese(translation.Title) ?? Chinese(Project.Title)
        ?? (!string.IsNullOrWhiteSpace(Project.Title) ? Project.Title : Strings.Resources_ChineseTitleUnavailable);
    public string Description => Chinese(translation.Description) ?? Chinese(Project.Description)
        ?? (!string.IsNullOrWhiteSpace(Project.Description) ? Project.Description : Strings.Resources_ChineseDescriptionEmpty);

    private static string? Chinese(string? value) => ResourceProjectTranslation.IsChinese(value) ? value : null;

    private async Task RefreshTranslationAsync()
    {
        if (localizer is null) return;
        ResourceProjectTranslation result;
        try { result = await localizer.LocalizeAsync(Project).ConfigureAwait(false); }
        catch (Exception) { result = new(); } // Provider failures must never become unobserved UI tasks.
        dispatcher.Invoke(() => { translation = result; NotifyText(); });
    }

    private void NotifyText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}
