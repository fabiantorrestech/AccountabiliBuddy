namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Goal reminders' state on this PC (round 2 R5.5; document "goals.reminderState", state of the moment: never synced, never in a
/// backup): each occurrence's mark by its key (reminder id and due time, or the nudge's goal and day). Ids and times only. Marks older
/// than <see cref="Kept"/> are pruned on every save.
/// </summary>
public sealed record GoalReminderState(IReadOnlyDictionary<string, GoalReminderMark> Marks)
{
    /// <summary>How long a mark is kept (a snooze into the next day still finds its mark).</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(3);

    public static GoalReminderState Empty { get; } = new(new Dictionary<string, GoalReminderMark>());

    public GoalReminderMark? MarkOf(string key) => Marks.TryGetValue(key, out GoalReminderMark? mark) ? mark : null;

    /// <summary>The state with <paramref name="key"/> marked, and marks older than <see cref="Kept"/> (from <paramref name="now"/>) dropped.</summary>
    public GoalReminderState With(string key, GoalReminderMark mark, DateTimeOffset now)
    {
        var marks = Marks.Where(pair => now - pair.Value.At <= Kept && pair.Key != key).ToDictionary(pair => pair.Key, pair => pair.Value);
        marks[key] = mark;
        return new GoalReminderState(marks);
    }
}
