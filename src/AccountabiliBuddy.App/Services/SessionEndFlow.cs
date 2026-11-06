using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Ends the current session, applying the friction dialog (wait + reason) when a focus phase is running.
/// Breaks and idle states end without friction. Confirmed during the wait, the end is armed instead (ADR 66) and the
/// session ends by itself when the wait is over.
/// </summary>
public sealed class SessionEndFlow
{
    private readonly SessionController _sessions;
    private readonly ArmedEndService _armedEnds;

    public SessionEndFlow(SessionController sessions, ArmedEndService armedEnds)
    {
        _sessions = sessions;
        _armedEnds = armedEnds;
    }

    /// <param name="owner">Window to center the dialog on, or null when invoked from the tray with no window open.</param>
    /// <returns>True if the session is now ended (or none was running); false when cancelled or armed to end later.</returns>
    public async Task<bool> TryEndSessionAsync(Window? owner)
    {
        if (!_sessions.State.IsActive)
        {
            return true;
        }

        // A break, a scheduled block or a session with no set end: nothing to wait for.
        if (!_sessions.IsEnforcing || _sessions.EndsOnCommand)
        {
            _sessions.EndSession(earlyEndReason: null);
            return true;
        }

        // Already armed: nothing to ask again; the countdown on Today and the tile has Cancel.
        if (_armedEnds.Find(ArmedEndKind.EndSession) is not null)
        {
            return false;
        }

        using var viewModel = new EndSessionDialogViewModel(_sessions.BypassSettings, _sessions.TimeProvider);
        var dialog = new EndSessionDialog { DataContext = viewModel };

        bool confirmed = owner is { IsVisible: true }
            ? await dialog.ShowDialog<bool>(owner)
            : await ShowWithoutOwnerAsync(dialog);

        if (!confirmed || !_sessions.State.IsActive)
        {
            return !_sessions.State.IsActive;
        }

        if (viewModel.Armed
            && _armedEnds.Arm(ArmedEndKind.EndSession, viewModel.RequestedAt, viewModel.Wait, viewModel.Reason, _sessions.State.ProfileId) is not null)
        {
            return false;
        }

        // Confirmed after the wait (or armed just as it ran out): ends now, as before.
        _sessions.EndSession(viewModel.Reason.Trim());
        return true;
    }

    private static Task<bool> ShowWithoutOwnerAsync(EndSessionDialog dialog)
    {
        var completion = new TaskCompletionSource<bool>();
        dialog.Closed += (_, _) => completion.TrySetResult(dialog.Confirmed);
        dialog.Show();
        dialog.Activate();
        return completion.Task;
    }
}
