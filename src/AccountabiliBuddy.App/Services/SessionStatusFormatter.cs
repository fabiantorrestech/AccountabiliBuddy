using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>Turns a <see cref="SessionState"/> into short human-readable text for the window and tray.</summary>
public static class SessionStatusFormatter
{
    public static string Title(SessionState state, string? profileName)
    {
        if (!state.IsActive)
        {
            return "No session running";
        }

        string phase = state.Phase switch
        {
            // A scheduled block blocks, but only sessions you start count as focus.
            SessionPhase.Focus when state.Mode == SessionMode.Scheduled => "Blocks on",
            SessionPhase.Focus => "Focus",
            SessionPhase.Planning => "Planning",
            SessionPhase.ShortBreak => "Short break",
            SessionPhase.LongBreak => "Long break",
            _ => "Idle",
        };

        return profileName is null ? phase : $"{phase} · {profileName}";
    }

    public static string Detail(SessionState state, DateTimeOffset now)
    {
        if (!state.IsActive)
        {
            return "Start a session below.";
        }

        string mode = state.IsTest ? "Test session" : state.Mode switch
        {
            SessionMode.Pomodoro => $"Pomodoro · {state.CompletedFocusCycles} cycle(s) done",
            SessionMode.Scheduled => "Scheduled block",
            _ => "Manual session",
        };

        // Stepped away or on a side task: the countdown is paused.
        string away = state.Away switch
        {
            AwayKind.SideTask => "Side task · ",
            not null => "Away · ",
            null => string.Empty,
        };
        DateTimeOffset clock = state.CountdownTime(now);
        if (state.IsPlanning && state.PhaseEndsAt is DateTimeOffset planningEnd)
        {
            // Planning is time to get ready, not focus: its own time left (0:00 once up, until the user answers).
            return $"{away}{FormatDuration(planningEnd - clock)} of planning left · {mode}";
        }

        if (state.PhaseEndsAt is DateTimeOffset end)
        {
            return $"{away}{FormatDuration(end - clock)} left · {mode}";
        }

        TimeSpan elapsed = state.PhaseStartedAt is DateTimeOffset start ? clock - start : TimeSpan.Zero;
        return $"{away}Running for {FormatDuration(elapsed)} · {mode}";
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
