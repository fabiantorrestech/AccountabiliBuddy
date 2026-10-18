using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Ends the current session, applying the friction dialog (wait + reason) when a focus phase is running.
/// Breaks and idle states end without friction.
/// </summary>
public sealed class SessionEndFlow
{
    private readonly SessionController _sessions;

    public SessionEndFlow(SessionController sessions)
    {
        _sessions = sessions;
    }

    /// <param name="owner">Window to center the dialog on, or null when invoked from the tray with no window open.</param>
    /// <returns>True if the session is now ended (or none was running).</returns>
    public async Task<bool> TryEndSessionAsync(Window? owner)
    {
        if (!_sessions.State.IsActive)
        {
            return true;
        }

        if (!_sessions.IsEnforcing)
        {
            _sessions.EndSession(earlyEndReason: null);
            return true;
        }

        string? note = _sessions.EndEarlyWaitMatchesUnlock
            ? "This is a scheduled block, so the wait matches the profile's shortest unlock option: ending the block isn't quicker than unlocking."
            : null;
        using var viewModel = new EndSessionDialogViewModel(_sessions.BypassSettings, _sessions.TimeProvider, note);
        var dialog = new EndSessionDialog { DataContext = viewModel };

        bool confirmed = owner is { IsVisible: true }
            ? await dialog.ShowDialog<bool>(owner)
            : await ShowWithoutOwnerAsync(dialog);

        if (!confirmed)
        {
            return false;
        }

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
