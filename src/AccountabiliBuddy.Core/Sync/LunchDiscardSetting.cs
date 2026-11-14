namespace AccountabiliBuddy.Core.Sync;

/// <summary>What <see cref="LunchDiscardSetting.Change"/> did with a wish.</summary>
public enum LunchDiscardOutcome
{
    /// <summary>The wish is in effect now (it was the same, or stricter).</summary>
    Applied,

    /// <summary>A looser wish is waiting for the looser-change delay.</summary>
    Waiting,

    /// <summary>A looser wish was refused: a focus session runs.</summary>
    Refused,
}

/// <summary>
/// "Allow discarding a lunch" for one mode (ADR 88; the phone's Kotlin twin is the same rule). On by default. Turning it off is stricter: it applies
/// at once and drops a waiting "on". Turning it on is looser: it waits the looser-change delay (a pending change, as the other settings that
/// loosen) and is refused while a focus session runs; a waiting change takes effect only when no session runs. Each mode keeps its own copy and
/// each device its own (ADR 69); the copy of the active mode is the one the lunch card reads.
/// </summary>
/// <param name="Allowed">Discarding a lunch is offered.</param>
/// <param name="Pending">A looser value (on) waiting for the delay; null when nothing waits.</param>
/// <param name="PendingFrom">When <paramref name="Pending"/> takes effect (trusted time), unless a session runs then.</param>
public sealed record LunchDiscardSetting(bool Allowed = true, bool? Pending = null, DateTimeOffset? PendingFrom = null)
{
    public static LunchDiscardSetting Default { get; } = new();

    /// <summary>On is looser than off.</summary>
    public static bool IsLooser(bool from, bool to) => to && !from;

    /// <summary>Asks for <paramref name="wanted"/>. Stricter or the same: applied now. Looser: refused while <paramref name="sessionRunning"/>, else waiting <paramref name="wait"/> (a wait already running for it keeps its time).</summary>
    public (LunchDiscardSetting Setting, LunchDiscardOutcome Outcome) Change(bool wanted, DateTimeOffset now, TimeSpan wait, bool sessionRunning)
    {
        if (!IsLooser(Allowed, wanted))
        {
            return (new LunchDiscardSetting(wanted), LunchDiscardOutcome.Applied);
        }

        if (sessionRunning)
        {
            return (this, LunchDiscardOutcome.Refused);
        }

        if (Pending == wanted && PendingFrom is not null)
        {
            return (this, LunchDiscardOutcome.Waiting);
        }

        return (this with { Pending = wanted, PendingFrom = now + wait }, LunchDiscardOutcome.Waiting);
    }

    /// <summary>A looser change that waited long enough takes effect, only while no session runs.</summary>
    public LunchDiscardSetting ApplyIfReady(DateTimeOffset now, bool sessionRunning) =>
        !sessionRunning && Pending is bool pending && PendingFrom is DateTimeOffset from && now >= from ? new LunchDiscardSetting(pending) : this;
}
