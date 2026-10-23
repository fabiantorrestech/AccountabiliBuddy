namespace AccountabiliBuddy.App.Services;

/// <summary>The hyperfocus reminder (document "hyperfocus").</summary>
/// <param name="IntervalMinutes">Minutes of unbroken focus before the reminder; 0 turns it off. 90 by default.</param>
/// <param name="OffForProfiles">Profiles (by id) that never get it.</param>
public sealed record HyperfocusSettings(int IntervalMinutes, IReadOnlyList<Guid> OffForProfiles)
{
    public static HyperfocusSettings Default { get; } = new(90, Array.Empty<Guid>());
}
