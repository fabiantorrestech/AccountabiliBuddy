namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>Where this PC's goals stand after the startup step (<see cref="GoalsMigration"/>).</summary>
public enum GoalModelState
{
    /// <summary>
    /// The first-run step hasn't made the goals yet (it failed or didn't balance; no marker). Nothing else changed: the daily goal
    /// (document "goal") works as before, and the next start tries again. Goals can't be edited meanwhile.
    /// </summary>
    NotMigrated,

    /// <summary>The goals store is the source: Deep work and the user's goals are read and saved.</summary>
    Ready,

    /// <summary>
    /// The marker says the goals were made, but their documents can't be read (for example sealed by another Windows account). The
    /// goals are never rebuilt on a guess: they show empty, can't be changed, and nothing (an edit, a restore) is saved over them.
    /// </summary>
    Unreadable,
}
