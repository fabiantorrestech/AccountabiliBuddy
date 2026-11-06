using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// An armed "Stop for today" (ADR 27, 66): the work-hours guard stops until tomorrow when the wait is over, with the reason given.
/// Dropped when there is nothing left to stop (turned off, stopped already, or today's work is over).
/// </summary>
public sealed class StopForTodayArmedEndHandler : IArmedEndHandler
{
    private readonly WorkHoursGuard _guard;

    public StopForTodayArmedEndHandler(WorkHoursGuard guard)
    {
        _guard = guard;
    }

    public ArmedEndKind Kind => ArmedEndKind.StopForToday;

    public string Label(ArmedEnd end) => "Work-hours guard stops";

    public string DoneTitle(ArmedEnd end) => "Work-hours guard stopped";

    public bool IsStillDue(ArmedEnd end) => _guard.HasWorkLeftToday;

    public async Task<string> ApplyAsync(ArmedEnd end, string reason)
    {
        return await _guard.StopForTodayAsync(reason, end.RequestedAt)
            ? "Stopped until tomorrow, as you confirmed."
            : "Not stopped: the reason couldn't be read. Stop for today again in Settings.";
    }
}
