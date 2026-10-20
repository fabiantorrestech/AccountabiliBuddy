using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The "N hidden" button under a History list, opening the hidden names with Show again.</summary>
public sealed partial class HiddenListViewModel : ObservableObject
{
    private readonly HiddenUsageService _hidden;

    public HiddenListViewModel(HiddenUsageService hidden)
    {
        _hidden = hidden;
        _hidden.Changed += (_, _) => Refresh();
    }

    public IReadOnlyList<string> Names => _hidden.HiddenNames.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public bool HasAny => _hidden.HiddenNames.Count > 0;

    public string ToggleText => IsOpen ? "Done" : $"{_hidden.HiddenNames.Count} hidden";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    public partial bool IsOpen { get; set; }

    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
    }

    [RelayCommand]
    private Task ShowAgainAsync(string name)
    {
        return _hidden.SetHiddenAsync(new[] { name }, hidden: false);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Names));
        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(ToggleText));
        if (!HasAny)
        {
            IsOpen = false;
        }
    }
}
