using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A pill of the Tasks page: a category filter, or an estimate choice (25 / 50 / 90 min). Selected = Ink on Surface2.</summary>
public sealed partial class TaskChipViewModel : ObservableObject
{
    private readonly Func<Task> _pick;

    public TaskChipViewModel(string text, bool isSelected, Func<Task> pick)
    {
        Text = text;
        IsSelected = isSelected;
        _pick = pick;
        PickCommand = new AsyncRelayCommand(() => _pick());
    }

    public string Text { get; }

    public bool IsSelected { get; }

    public ICommand PickCommand { get; }
}
