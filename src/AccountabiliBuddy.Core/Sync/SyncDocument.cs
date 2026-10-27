namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A shared setting as it travels, with when it was last edited: the newest edit wins, and each receiver applies it by
/// its own rules (a looser profile change still waits). Cold Turkey's expected blocks are never one (ADR 22).
/// </summary>
/// <param name="Key"><see cref="ProfilesKey"/>, <see cref="SleepKey"/>, <see cref="DayBarKey"/> or <see cref="GoalKey"/>.</param>
/// <param name="Json">The setting's JSON as the writing device stores it.</param>
public sealed record SyncDocument(string Key, string Json, DateTimeOffset EditedAt)
{
    public const string ProfilesKey = "profiles";
    public const string SleepKey = "sleep";
    public const string DayBarKey = "daybar";
    public const string GoalKey = "goal";
}
