using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Setup;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "Start over" dialog (Settings › App &amp; data, and "Start from scratch" on an unlinked PC; ADR 54, 10): says what is
/// cleared and what stays, asks the user to type "start over", then has <see cref="StartOverService"/> copy the database and
/// restarts the app, which clears the data on its way up and opens the first-run assistant. Refused during a focus session
/// the user started. Use from the UI thread only.
/// </summary>
public sealed partial class StartOverViewModel : ObservableObject, IDisposable
{
    private readonly StartOverService _service;
    private readonly SessionController _sessions;
    private readonly Action _restart;

    /// <param name="restart">Restarts the app (the data is cleared on the way up).</param>
    public StartOverViewModel(StartOverService service, SessionController sessions, Action restart)
    {
        _service = service;
        _sessions = sessions;
        _restart = restart;
        Typed = string.Empty;
        IsLocked = sessions.IsEnforcing;
        sessions.StateChanged += OnSessionStateChanged;
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The words the user types: "start over".</summary>
    public string Phrase => StartOverService.ConfirmPhrase;

    public string LockedText => StartOverService.LockedReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Typed { get; set; }

    /// <summary>A focus session the user started is running: starting over is refused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public bool CanStart => !IsLocked && !IsBusy && StartOverService.IsConfirmed(Typed);

    public void Dispose()
    {
        _sessions.StateChanged -= OnSessionStateChanged;
    }

    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        IsLocked = _sessions.IsEnforcing;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsBusy = true;
        try
        {
            string? problem = await _service.PrepareAsync(Typed);
            if (problem is not null)
            {
                Message = problem;
                return;
            }

            Message = "Copied your data to the backups folder. Restarting…";
            _restart();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
