using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// An armed "End work early" (ADR 65, 66): when the wait is over the block the dialog showed ends (and the later blocks when that
/// was ticked), then the shutdown routine opens on Today. Like the dialog, it never applies during a session: a session started
/// meanwhile drops it, and so does the block ending first.
/// </summary>
public sealed class WorkEndArmedEndHandler : IArmedEndHandler
{
    private readonly WorkEndedService _workEnded;
    private readonly SessionController _sessions;
    private readonly Func<bool> _openShutdown;

    /// <param name="openShutdown">Opens the shutdown routine on Today without taking the focus; false when it can't open.</param>
    public WorkEndArmedEndHandler(WorkEndedService workEnded, SessionController sessions, Func<bool> openShutdown)
    {
        _workEnded = workEnded;
        _sessions = sessions;
        _openShutdown = openShutdown;
    }

    public ArmedEndKind Kind => ArmedEndKind.EndWorkEarly;

    public string Label(ArmedEnd end) => "Work ends";

    public string DoneTitle(ArmedEnd end) => "Work ended early";

    public bool IsStillDue(ArmedEnd end) =>
        end.WorkEnd is ArmedWorkEnd work
        && !_sessions.State.IsActive
        && TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime < work.Current.End;

    public async Task<string> ApplyAsync(ArmedEnd end, string reason)
    {
        if (end.WorkEnd is not ArmedWorkEnd work)
        {
            return "Nothing was ended.";
        }

        var proposal = new WorkEndProposal(work.PlanDate, work.Current, work.Later);
        if (!await _workEnded.EndAsync(proposal, work.AlsoEndLater, reason))
        {
            return "That work block was already over, so nothing was ended.";
        }

        return _openShutdown()
            ? "As you confirmed. The shutdown routine is open on Today."
            : "As you confirmed. Start shutdown on Today when you're ready.";
    }
}
