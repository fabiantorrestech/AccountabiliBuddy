namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Which question the away card asks.</summary>
public enum AwayCardKind
{
    /// <summary>"Side task: how long?" 5, 10 or 15 minutes.</summary>
    SideTaskLength,

    /// <summary>"Back to the task?" after input while stepped away: Back on track or Still away.</summary>
    BackOnTrack,

    /// <summary>"Back to the task?" when a side task's time is up: Back on track, +5 min or End session.</summary>
    SideTaskOver,
}
