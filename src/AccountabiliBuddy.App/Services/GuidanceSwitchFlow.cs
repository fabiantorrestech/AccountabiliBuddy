using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Switching to ProductaBuddy from Settings or the setup assistant (ADR 68). With no session running it is the usual looser-change
/// wait. During a focus session it asks: after this session ends (the wait, then no session running), or now, which costs what
/// ending the session early costs (its wait and a reason, armed in the background once confirmed, ADR 66) before the wait starts and
/// applies with the session still running. A session that ends on command (no set end, a break) costs nothing extra, as ending it does.
/// </summary>
public sealed class GuidanceSwitchFlow
{
    private readonly GuidanceModeService _guidance;
    private readonly SessionController _sessions;
    private readonly ArmedEndService _armedEnds;
    private readonly Func<TimeSpan> _looserWait;
    private readonly Func<Window?> _owner;

    public GuidanceSwitchFlow(
        GuidanceModeService guidance, SessionController sessions, ArmedEndService armedEnds, Func<TimeSpan> looserWait, Func<Window?> owner)
    {
        _guidance = guidance;
        _sessions = sessions;
        _armedEnds = armedEnds;
        _looserWait = looserWait;
        _owner = owner;
    }

    public async Task SwitchToCompanionAsync()
    {
        // Already there, or "now" already paid for (its countdown on Today and the tile has Cancel).
        if (_guidance.IsCompanion || _guidance.State.Pending is { DuringSession: true } || _armedEnds.Find(ArmedEndKind.Companion) is not null)
        {
            return;
        }

        if (!_sessions.State.IsActive)
        {
            await _guidance.RequestCompanionAsync();
            return;
        }

        bool free = !_sessions.IsEnforcing || _sessions.EndsOnCommand;
        BypassSettings bypass = _sessions.BypassSettings;
        string companionWait = Describe(_looserWait());
        string nowCost = free ? "nothing extra" : $"a {Describe(bypass.WaitDuration)} wait and a reason";
        var choice = new GuidanceDuringSessionViewModel(_sessions.State.HasSetEnd, nowCost, companionWait);
        var choiceDialog = new GuidanceDuringSessionDialog { DataContext = choice };
        if (!await ShowAsync(choiceDialog, () => choiceDialog.Confirmed))
        {
            return;
        }

        if (!_sessions.State.IsActive)
        {
            await _guidance.RequestCompanionAsync(); // The session ended meanwhile: the usual wait.
            return;
        }

        if (choice.AfterSessionChosen)
        {
            await _guidance.RequestCompanionAfterSessionAsync();
            return;
        }

        if (free)
        {
            await _guidance.RequestCompanionNowAsync(string.Empty);
            return;
        }

        using var cost = new EndSessionDialogViewModel(bypass, _sessions.TimeProvider, wording: EndDialogWording.Companion(AppBrand.CompanionName, companionWait));
        var costDialog = new EndSessionDialog { DataContext = cost };
        if (!await ShowAsync(costDialog, () => costDialog.Confirmed))
        {
            return;
        }

        if (cost.Armed && _armedEnds.Arm(ArmedEndKind.Companion, cost.RequestedAt, cost.Wait, cost.Reason, _sessions.State.ProfileId) is not null)
        {
            return;
        }

        // Confirmed after the wait (or armed just as it ran out): the switch's own wait starts now.
        await _guidance.RequestCompanionNowAsync(cost.Reason);
    }

    // "10 minutes", "60 s", "1 h 30 min".
    private static string Describe(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return $"{(int)span.TotalSeconds} s";
        }

        if (span < TimeSpan.FromHours(1))
        {
            int minutes = (int)Math.Round(span.TotalMinutes);
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        return span.Minutes == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalHours} h {span.Minutes} min";
    }

    private async Task<bool> ShowAsync(Window dialog, Func<bool> confirmed)
    {
        if (_owner() is { IsVisible: true } owner)
        {
            await dialog.ShowDialog<bool>(owner);
            return confirmed();
        }

        var completion = new TaskCompletionSource<bool>();
        dialog.Closed += (_, _) => completion.TrySetResult(confirmed());
        dialog.Show();
        dialog.Activate();
        return await completion.Task;
    }
}
