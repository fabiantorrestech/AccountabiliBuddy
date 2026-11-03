using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day plan category in the quit safeguard's card: ticked, Quit is locked during it.</summary>
public sealed partial class QuitCategoryRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;

    public QuitCategoryRowViewModel(Guid id, string name, IBrush brush, bool isTicked, Action changed)
    {
        Id = id;
        Name = name;
        Brush = brush;
        _changed = changed;
        _loading = true;
        IsTicked = isTicked;
        _loading = false;
    }

    public Guid Id { get; }

    public string Name { get; }

    public IBrush Brush { get; }

    [ObservableProperty]
    public partial bool IsTicked { get; set; }

    /// <summary>Shows what the service holds, without counting as a change by the user.</summary>
    public void Show(bool isTicked)
    {
        _loading = true;
        IsTicked = isTicked;
        _loading = false;
    }

    partial void OnIsTickedChanged(bool value)
    {
        if (!_loading)
        {
            _changed();
        }
    }
}
