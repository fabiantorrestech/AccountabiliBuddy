namespace AccountabiliBuddy.App.Services;

/// <summary>The screens of the shutdown routine, in order. <see cref="Closing"/> always comes last and cannot be switched off.</summary>
public enum ShutdownStep
{
    Capture,
    Unfinished,
    LookAhead,
    PlanTomorrow,
    Review,
    Closing,
}
