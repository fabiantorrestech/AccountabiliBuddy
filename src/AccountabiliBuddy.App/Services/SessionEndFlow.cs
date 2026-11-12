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

    /// <summary>
    /// "End and mark the rest unfinished" (Tasks redesign): the session's tasks are told just before the end, so its open tasks go back to
    /// Queued (set by the app; null before the task list, when the option isn't offered).
    /// </summary>
    public Func<Task>? MarkRestUnfinished { get; set; }

    /// <param name="owner">Window to center the dialog on, or null when invoked from the tray with no window open.</param>
    /// <param name="unfinishedToQueued">
    /// "End and mark the rest unfinished": the same end (the same wait and reason, armed the same way); only when it happens, the session's
    /// open tasks go back to Queued. It never ends a session sooner or without the reason.
    /// </param>
    /// <returns>True if the session is now ended (or none was running); false when cancelled or armed to end later.</returns>
    public async Task<bool> TryEndSessionAsync(Window? owner, bool unfinishedToQueued = false)
    {
        if (!_sessions.State.IsActive)
        {
            return true;
        }

        bool toQueued = unfinishedToQueued && MarkRestUnfinished is not null;

        // A break, a scheduled block or a session with no set end: nothing to wait for.
        if (!_sessions.IsEnforcing || _sessions.EndsOnCommand)
        {
            await MarkIfAsked(toQueued);
            _sessions.EndSession(earlyEndReason: null);
            return true;
        }

        // Already armed: nothing to ask again; the countdown on Today and the tile has Cancel.
        if (_armedEnds.Find(ArmedEndKind.EndSession) is not null)
        {
            return false;
        }

        string? note = toQueued ? "When it ends, this session's unfinished tasks go back to Queued." : null;
        using var viewModel = new EndSessionDialogViewModel(_sessions.BypassSettings, _sessions.TimeProvider, note);
        var dialog = new EndSessionDialog { DataContext = viewModel };

        bool confirmed = owner is { IsVisible: true }
            ? await dialog.ShowDialog<bool>(owner)
            : await ShowWithoutOwnerAsync(dialog);

        if (!confirmed || !_sessions.State.IsActive)
        {
            return !_sessions.State.IsActive;
        }

        if (viewModel.Armed
            && _armedEnds.Arm(
                ArmedEndKind.EndSession, viewModel.RequestedAt, viewModel.Wait, viewModel.Reason, _sessions.State.ProfileId,
                unfinishedToQueued: toQueued) is not null)
        {
            return false;
        }

        // Confirmed after the wait (or armed just as it ran out): ends now, as before.
        await MarkIfAsked(toQueued);
        _sessions.EndSession(viewModel.Reason.Trim());
        return true;
    }

    private Task MarkIfAsked(bool toQueued)
    {
        return toQueued ? MarkRestUnfinished!() : Task.CompletedTask;
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
