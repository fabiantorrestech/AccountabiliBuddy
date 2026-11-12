namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>Which of Deep work's waiting changes to cancel (<see cref="GoalsService.CancelDeepWorkPendingAsync"/>).</summary>
public enum DeepWorkTargetKind
{
    /// <summary>A lower daily target (the "goal" document's minutes).</summary>
    Daily,

    /// <summary>A lower weekly target, or none.</summary>
    Weekly,

    /// <summary>"Track time only, no target".</summary>
    TrackTimeOnly,
}
