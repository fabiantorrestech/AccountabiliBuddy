using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Come back to" on Today (idle and in Focus mode): the open come-backs, oldest first, each with a box to tick it, ✎ to
/// edit it and "Add to tasks" to bring it into the checklist; and a line to add one.
/// </summary>
public sealed partial class ComebacksViewModel : ObservableObject
{
    private readonly ComebackStore _store;
    private readonly SessionTasksService? _tasks;

    /// <param name="tasks">The checklist a come-back can be added to; null hides "Add to tasks".</param>
    public ComebacksViewModel(ComebackStore store, SessionTasksService? tasks = null)
    {
        _store = store;
        _tasks = tasks;
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
            Items.Add(new ComebackRowViewModel(comeback, _store, _tasks));
        }

        HasItems = Items.Count > 0;
    }
}
