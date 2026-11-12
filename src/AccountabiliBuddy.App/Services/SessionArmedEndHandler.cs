using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>An armed "End session early" (ADR 66): the session ends when the wait is over, as the end dialog would have ended it.</summary>
public sealed class SessionArmedEndHandler : IArmedEndHandler
{
    private readonly SessionController _sessions;

    public SessionArmedEndHandler(SessionController sessions)
    {
        _sessions = sessions;
    }

    public ArmedEndKind Kind => ArmedEndKind.EndSession;

    public string Label(ArmedEnd end) => _sessions.State.IsTest ? "Test ends" : "Session ends";

    public string DoneTitle(ArmedEnd end) => "Session ended";

    public bool IsStillDue(ArmedEnd end) => _sessions.State.IsActive && _sessions.State.ProfileId == end.ProfileId;

    /// <summary>"End and mark the rest unfinished": tells the session's tasks just before the end (set by the app; null before the task list).</summary>
    public Func<Task<int>>? MarkRestUnfinished { get; set; }

    public async Task<string> ApplyAsync(ArmedEnd end, string reason)
    {
        int returned = end.UnfinishedToQueued && MarkRestUnfinished is not null ? await MarkRestUnfinished() : 0;
        _sessions.EndSession(reason);
        return returned switch
        {
            0 => "Your focus session ended early, as you confirmed.",
            1 => "Your focus session ended early, as you confirmed. Its unfinished task is back in Queued.",
            _ => $"Your focus session ended early, as you confirmed. Its {returned} unfinished tasks are back in Queued.",
        };
    }
}
