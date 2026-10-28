using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The quiet bar at the bottom of a page or card after a delete: "Deleted “Draft Q4…” · Undo", for 10 seconds. Undo puts the
/// item back where it was (same id, place, steps, note, estimate and category); what it can't do (a 4th task) it says. The item
/// stays in Recently deleted for 30 days either way. Nothing here is logged: the text is the user's.
/// </summary>
public sealed partial class UndoBarViewModel : ObservableObject, IDisposable
{
    /// <summary>How long the bar stays.</summary>
    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(10);

    private const int ShortTitleLength = 24;

    private readonly SessionTasksService _tasks;
    private readonly RecentlyDeletedStore? _store;
    private CancellationTokenSource? _hide;
    private Guid? _entryId;

    public UndoBarViewModel(SessionTasksService tasks)
    {
        _tasks = tasks;
        _store = tasks.Deleted;
        Text = Message = string.Empty;
        if (_store is not null)
        {
            _store.Recorded += OnRecorded;
            _store.Changed += OnStoreChanged;
        }
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>E.g. "Deleted “Draft Q4…”".</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>Why Undo could not put it back, e.g. "This session already has 3 tasks."</summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    public bool HasMessage => Message.Length > 0;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    public void Dispose()
    {
        if (_store is not null)
        {
            _store.Recorded -= OnRecorded;
            _store.Changed -= OnStoreChanged;
        }

        _hide?.Cancel();
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_entryId is not Guid id)
        {
            return;
        }

        (bool restored, string? message) = await _tasks.RestoreAsync(id);
        if (restored)
        {
            Hide();
        }
        else
        {
            Message = message ?? string.Empty;
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        Hide();
    }

    private void OnRecorded(object? sender, DeletedItem item)
    {
        _entryId = item.EntryId;
        Text = $"Deleted “{Short(item.Title)}”";
        Message = string.Empty;
        IsVisible = true;
        _ = HideLaterAsync();
    }

    // Another bar (or Recently deleted) restored it: this one goes too.
    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (_entryId is Guid id && _store?.Find(id) is null)
        {
            Hide();
        }
    }

    private async Task HideLaterAsync()
    {
        _hide?.Cancel();
        var cancel = new CancellationTokenSource();
        _hide = cancel;
        try
        {
            await Task.Delay(Shown, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Hide();
    }

    private void Hide()
    {
        _hide?.Cancel();
        IsVisible = false;
        Message = string.Empty;
        _entryId = null;
    }

    private static string Short(string title)
    {
        string trimmed = title.Trim();
        return trimmed.Length <= ShortTitleLength ? trimmed : trimmed[..ShortTitleLength].TrimEnd() + "…";
    }
}
