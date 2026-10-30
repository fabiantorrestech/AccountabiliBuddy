using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A pill of the Tasks page: a category filter (with its colour dot), or an estimate choice (25 / 50 / 90 min). Selected = Ink on Surface2.</summary>
public sealed partial class TaskChipViewModel : ObservableObject
{
    private readonly Func<Task> _pick;

    public TaskChipViewModel(string text, bool isSelected, Func<Task> pick, IBrush? dot = null)
    {
        Dot = dot;
        Text = text;
        IsSelected = isSelected;
        _pick = pick;
        PickCommand = new AsyncRelayCommand(() => _pick());
    }

    public string Text { get; }

    /// <summary>A category's colour, drawn as a small dot before the text; null for All and the estimate chips.</summary>
    public IBrush? Dot { get; }

    public bool HasDot => Dot is not null;

    public bool IsSelected { get; }

    public ICommand PickCommand { get; }
}
