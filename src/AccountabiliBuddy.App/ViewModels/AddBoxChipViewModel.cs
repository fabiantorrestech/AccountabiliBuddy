using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A label or tag picked in an add box, shown as a removable chip ("● AI ×", "#home ×").</summary>
public sealed partial class AddBoxChipViewModel
{
    private readonly Action _remove;

    public AddBoxChipViewModel(string text, IBrush? dot, Action remove)
    {
        Text = text;
        Dot = dot;
        _remove = remove;
    }

    public string Text { get; }

    /// <summary>The label's colour; null for a tag.</summary>
    public IBrush? Dot { get; }

    public bool HasDot => Dot is not null;

    public string RemoveName => "Remove " + Text;

    [RelayCommand]
    private void Remove() => _remove();
}
