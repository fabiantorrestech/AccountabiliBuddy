using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Link;

namespace AccountabiliBuddy.App.Services.Remote;

/// <summary>
/// Starting and ending the PC's session from another device, by the PC's own rules, for both ways a phone reaches the
/// PC: the link (ADR 14) and the sync files (ADR 42). A start is refused while a session runs; ending early takes the
/// End session dialog's wait and reason, except for breaks and sessions that end on command
/// (<see cref="BypassPolicy.EndsOnCommand"/>). UI thread only: the callers move there.
/// </summary>
public sealed class RemoteSessionControl
{
    /// <summary>The longest length a device can start (12 hours).</summary>
    public const int MaxMinutes = 12 * 60;

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly QuickSessionService _quickSession;

    public RemoteSessionControl(SessionController sessions, ConfigurationService configuration, QuickSessionService quickSession)
    {
        _sessions = sessions;
        _configuration = configuration;
        _quickSession = quickSession;
    }

    /// <summary>Starts <paramref name="profileId"/> for <paramref name="minutes"/>.</summary>
    /// <returns>Null when it started; otherwise why not.</returns>
    public string? Start(Guid? profileId, int? minutes)
    {
        if (_sessions.State.IsActive)
        {
            return RunningMessage;
        }

        if (!Exists(profileId))
        {
            return MissingProfileMessage;
        }

        if (minutes is not int length || length < 1 || length > MaxMinutes)
        {
            return "Pick a length between 1 minute and 12 hours.";
        }

        return _sessions.StartManual(profileId!.Value, TimeSpan.FromMinutes(length)) ? null : "The session couldn't start.";
    }

    /// <summary>"Quick Start with Default": the default profile for the remembered focus length (it counts down), made on first use.</summary>
    /// <returns>Null when it started; otherwise why not.</returns>
    public async Task<string?> StartWithDefaultAsync()
    {
        if (_sessions.State.IsActive)
        {
            return RunningMessage;
        }

        return await _quickSession.StartWithDefaultAsync() ? null : "The Quick Start profile couldn't be set up on the PC.";
    }

    /// <summary>"Quick Start · <paramref name="profileId"/>": that profile for the remembered focus length.</summary>
    /// <returns>Null when it started; otherwise why not.</returns>
    public string? StartQuick(Guid profileId)
    {
        if (_sessions.State.IsActive)
        {
            return RunningMessage;
        }

        if (!Exists(profileId))
        {
            return MissingProfileMessage;
        }

        return _quickSession.StartQuick(profileId) ? null : "The session couldn't start.";
    }

    /// <summary>The profile "Quick Start with Default" starts (the default profile); null when it isn't made yet.</summary>
    public Guid? CountUpProfileId => _quickSession.CountUpProfileId;

    /// <summary>What ending the running session early takes now; null when none runs.</summary>
    public LinkEndEarlyTerms? EndEarlyTerms()
    {
        SessionState state = _sessions.State;
        if (!state.IsActive)
        {
            return null;
        }

        DateTimeOffset now = _sessions.Now;
        if (!state.IsEnforcing)
        {
            return new LinkEndEarlyTerms(0, 0, now, "On a break: it ends right away.");
        }

        if (_sessions.EndsOnCommand)
        {
            return new LinkEndEarlyTerms(0, 0, now, state.Mode == SessionMode.Scheduled
                ? "A scheduled block: it ends right away (your blocker still applies its own blocks)."
                : "No set end: it ends right away.");
        }

        BypassSettings bypass = _sessions.BypassSettings;
        return new LinkEndEarlyTerms((int)Math.Ceiling(bypass.WaitDuration.TotalSeconds), bypass.MinReasonLength, now + bypass.WaitDuration, null);
    }

    /// <summary>
    /// Ends the running session. <paramref name="reason"/> is recorded as the early-end reason only for a focus phase
    /// that doesn't end on command. The caller has checked the wait and the reason's length.
    /// </summary>
    /// <param name="notice">For the "ended from your phone" notice: the reason, or "Ended on command."; null after a break.</param>
    /// <returns>False when no session runs.</returns>
    public bool TryEnd(string reason, out string? notice)
    {
        notice = null;
        if (!_sessions.State.IsActive)
        {
            return false;
        }

        bool wasFocus = _sessions.IsEnforcing;
        bool onCommand = _sessions.EndsOnCommand;
        _sessions.EndSession(wasFocus && !onCommand ? reason : null);
        if (wasFocus)
        {
            notice = onCommand ? "Ended on command." : reason;
        }

        return true;
    }

    private const string RunningMessage = "A session is already running on the PC.";

    private const string MissingProfileMessage = "That profile isn't on the PC any more.";

    private bool Exists(Guid? profileId) => profileId is Guid id && _configuration.Current.Profiles.Any(profile => profile.Id == id);
}
