namespace AccountabiliBuddy.App.Services;

/// <summary>What "Add to Today" did (ADR 25: the three-task rule never loosens).</summary>
public enum AddToTodayResult
{
    /// <summary>The task joined today's list (the next session's, or the running checklist).</summary>
    Added,

    /// <summary>Today already holds its three tasks: nothing was added; the caller asks to add it anyway (with a reason) or to swap.</summary>
    AtLimit,

    /// <summary>"Add anyway" was asked for with a reason shorter than <see cref="SessionTasksService.MinOverLimitReasonLength"/>.</summary>
    ReasonTooShort,

    /// <summary>The source is gone, or has no title: nothing happened.</summary>
    NothingToAdd,
}
