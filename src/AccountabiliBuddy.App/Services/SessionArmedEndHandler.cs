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

    public Task<string> ApplyAsync(ArmedEnd end, string reason)
    {
        _sessions.EndSession(reason);
        return Task.FromResult("Your focus session ended early, as you confirmed.");
    }
}
