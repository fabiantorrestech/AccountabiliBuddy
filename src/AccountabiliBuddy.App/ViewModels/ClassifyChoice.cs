namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The answer picked in the single classify prompt's toggle; Done applies it.</summary>
public enum ClassifyChoice
{
    /// <summary>Blocked from now on (the reason is optional).</summary>
    Distracting,

    /// <summary>Allowed for this session (needs the reason).</summary>
    OnTrack,

    /// <summary>Always allowed in the session's profile and any ticked (needs the reason).</summary>
    Exempt,
}
