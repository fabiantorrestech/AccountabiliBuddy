using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// An on/off chip of Search filters (Tasks redesign 7.15, 7.29): a Look in or Bins chip ("✓ Steps" on, "✕ Steps" off), or a When choice
/// (one picked at a time; no mark).
/// </summary>
public sealed partial class SearchFilterToggleViewModel : ObservableObject
{
    public SearchFilterToggleViewModel(string key, string name, bool marked, Action<SearchFilterToggleViewModel> click)
    {
        Key = key;
        Name = name;
        IsMarked = marked;
        ClickCommand = new RelayCommand(() => click(this));
    }

    /// <summary>What it stands for ("steps", "next", "thisWeek").</summary>
    public string Key { get; }

    public string Name { get; }

    /// <summary>A Look in or Bins chip ("✓" / "✕" before the name); false for a When choice.</summary>
    public bool IsMarked { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(AccessibleName))]
    public partial bool IsOn { get; set; }

    /// <summary>"✓ Steps", "✕ Steps", or the plain name for a When choice.</summary>
    public string Text => IsMarked ? (IsOn ? "✓ " : "✕ ") + Name : Name;

    /// <summary>"Steps, on".</summary>
    public string AccessibleName => Name + (IsOn ? ", on" : ", off");

    public ICommand ClickCommand { get; }
}
