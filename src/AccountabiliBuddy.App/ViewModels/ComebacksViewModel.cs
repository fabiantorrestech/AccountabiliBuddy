using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>"Come back to" on Today: the open come-backs, oldest first, each with a box to tick it; and a line to add one.</summary>
public sealed partial class ComebacksViewModel : ObservableObject
{
    private readonly ComebackStore _store;

    public ComebacksViewModel(ComebackStore store)
    {
        _store = store;
        NewText = string.Empty;
        _store.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<ComebackRowViewModel> Items { get; } = new();

    [ObservableProperty]
    public partial bool HasItems { get; set; }

    /// <summary>The add line (Enter adds).</summary>
    [ObservableProperty]
    public partial string NewText { get; set; }

    [RelayCommand]
    private async Task AddAsync()
    {
        string text = NewText;
        NewText = string.Empty;
        await _store.AddAsync(text);
    }

    private void Refresh()
    {
        Items.Clear();
        foreach (Comeback comeback in _store.Open)
        {
            Items.Add(new ComebackRowViewModel(comeback, id => _ = _store.TickAsync(id)));
        }

        HasItems = Items.Count > 0;
    }
}
