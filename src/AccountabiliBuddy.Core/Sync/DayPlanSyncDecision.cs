namespace AccountabiliBuddy.Core.Sync;

/// <summary>What to do with a day plan that arrives from another device (<see cref="DayPlanClash.Decide"/>).</summary>
public enum DayPlanSyncDecision
{
    /// <summary>Both devices hold the same plan.</summary>
    Same,

    /// <summary>The other device edited the plan this device holds: take its plan.</summary>
    TakeIncoming,

    /// <summary>This device edited the plan the other device holds: keep this one.</summary>
    KeepLocal,

    /// <summary>
    /// Both changed it before they synced: ask which to keep ("Keep the one I was using" / "Use the latest"); closed or
    /// ignored, the latest edit wins (<see cref="DayPlanClash.Latest"/>).
    /// </summary>
    Ask,
}
