using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of the "From your lists" picker: a list's name above its items (a header), or an item that can be taken.</summary>
public sealed partial class ListPickRowViewModel : ObservableObject
{
    private readonly Func<Task>? _pick;

    /// <summary>A list's name.</summary>
    public ListPickRowViewModel(string title)
    {
        Title = title;
        Detail = string.Empty;
        ListName = string.Empty;
        Brush = Brushes.Transparent;
        CategoryName = string.Empty;
        IsHeader = true;
    }

    /// <summary>An item: its text, a line under it (due date, steps), its category's colour (null: none) and what choosing it does.</summary>
    public ListPickRowViewModel(string title, string detail, IBrush? categoryBrush, string? categoryName, Func<Task> pick, string listName = "")
    {
        Title = title;
        Detail = detail;
        ListName = listName;
        Brush = categoryBrush ?? Brushes.Transparent;
        HasCategory = categoryBrush is not null;
        CategoryName = categoryName ?? string.Empty;
        _pick = pick;
    }

    public string Title { get; }

    /// <summary>E.g. "Due Nov 5 · 2 steps"; empty when there is nothing to say.</summary>
    public string Detail { get; }

    public bool HasDetail => Detail.Length > 0;

    /// <summary>The list it is in, for a suggestion under a box.</summary>
    public string ListName { get; }

    /// <summary>A suggestion's second line: "Later · Due Nov 5".</summary>
    public string SuggestionDetail => Detail.Length > 0 ? $"{ListName} · {Detail}" : ListName;

    public IBrush Brush { get; }

    public bool HasCategory { get; }

    /// <summary>The category's name, for the tip and the screen reader; empty for none.</summary>
    public string CategoryName { get; }

    public bool IsHeader { get; }

    public bool IsItem => !IsHeader;

    /// <summary>The suggestion the arrow keys are on.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private Task PickAsync()
    {
        return _pick?.Invoke() ?? Task.CompletedTask;
    }
}
