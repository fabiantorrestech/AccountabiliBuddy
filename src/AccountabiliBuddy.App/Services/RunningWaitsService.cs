using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Every wait running in the background, in one list (ADR 66), for the tile's line, Today's banner and the tray's tooltip.
/// Each source reports its own; the list is read fresh each time, so it never holds a stale wait.
/// </summary>
public sealed class RunningWaitsService
{
    private readonly SessionController _sessions;
    private readonly List<Func<IEnumerable<RunningWait>>> _sources = [];

    public RunningWaitsService(SessionController sessions)
    {
        _sessions = sessions;
    }

    /// <summary>Raised by a source when its waits changed (armed, cancelled, applied), so Today redraws without waiting for a tick.</summary>
    public event EventHandler? Changed;

    public void AddSource(Func<IEnumerable<RunningWait>> source)
    {
        _sources.Add(source);
    }

    public void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The waits still running, the first to end first.</summary>
    public IReadOnlyList<RunningWait> Current()
    {
        DateTimeOffset now = _sessions.Now;
        return _sources.SelectMany(source => source()).Where(wait => wait.ReadyAt > now).OrderBy(wait => wait.ReadyAt).ToList();
    }

    /// <summary>"Session ends in 0:42" for one wait.</summary>
    public string Describe(RunningWait wait) => $"{wait.Label} in {ArmedEndRules.Countdown(wait.ReadyAt - _sessions.Now)}";

    /// <summary>The first wait's line, with "+1 more" when others run; null when none runs.</summary>
    public string? Line()
    {
        IReadOnlyList<RunningWait> waits = Current();
        if (waits.Count == 0)
        {
            return null;
        }

        return waits.Count == 1 ? Describe(waits[0]) : $"{Describe(waits[0])} · +{waits.Count - 1} more";
    }
}
