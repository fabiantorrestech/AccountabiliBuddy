using AccountabiliBuddy.Core.Import;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A site or app from the other blocker's usage statistics that can be added as a block.</summary>
public sealed partial class UsageSuggestionRowViewModel : ObservableObject
{
    public UsageSuggestionRowViewModel(ImportedUsage usage)
    {
        Usage = usage;
    }

    public ImportedUsage Usage { get; }

    public string Name => Usage.IsApp ? $"{Usage.Name} (app)" : Usage.Name;

    public string Time => Usage.Total.TotalHours >= 1 ? $"{Usage.Total.TotalHours:0.#} h" : $"{Usage.Total.TotalMinutes:0} min";

    /// <summary>The entry in paste-list syntax: apps get ".exe" so they become app rules, not sites.</summary>
    public string Entry => Usage.IsApp ? Usage.Name + ".exe" : Usage.Name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
