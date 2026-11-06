using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// An armed Pre-Work day off (ADR 31, 66): when the ten-minute wait is over, today becomes a day off on this computer and the
/// planned lines (sealed as the armed end's reason) become come-backs, as "Start my day off" does. Dropped when the ask was
/// undone, a day off is already on, a real session started (it ends Pre-Work), or the day changed.
/// </summary>
public sealed class PreWorkDayOffArmedEndHandler : IArmedEndHandler
{
    private readonly PreWorkService _preWork;
    private readonly SessionController _sessions;
    private readonly Func<string, Task> _addComeback;

    public PreWorkDayOffArmedEndHandler(PreWorkService preWork, SessionController sessions, Func<string, Task> addComeback)
    {
        _preWork = preWork;
        _sessions = sessions;
        _addComeback = addComeback;
    }

    public ArmedEndKind Kind => ArmedEndKind.PreWorkDayOff;

    public string Label(ArmedEnd end) => "Day off starts";

    public string DoneTitle(ArmedEnd end) => "Day off";

    public bool IsStillDue(ArmedEnd end) => _preWork.IsDayOffAskedToday && !_preWork.IsDayOff && !_sessions.State.IsRealSession;

    public async Task<string> ApplyAsync(ArmedEnd end, string reason)
    {
        await _preWork.TakeDayOffAsync(reason.Split('\n'), _addComeback);
        return _preWork.IsDayOff
            ? "Your day off started, as you confirmed. Your plans wait under Come back to."
            : "The day off didn't start. Ask again from the Pre-Work card.";
    }
}
