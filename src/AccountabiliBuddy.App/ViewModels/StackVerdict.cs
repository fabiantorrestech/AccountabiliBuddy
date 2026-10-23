namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A row's answer in the stacked prompt.</summary>
public enum StackVerdict
{
    /// <summary>Not answered: it stays unknown and is asked about as usual.</summary>
    Unanswered,

    /// <summary>Blocked from now on.</summary>
    Distracting,

    /// <summary>Allowed for this session (needs the reason).</summary>
    OnTrack,

    /// <summary>Always allowed (needs the reason).</summary>
    Exempt,
}
