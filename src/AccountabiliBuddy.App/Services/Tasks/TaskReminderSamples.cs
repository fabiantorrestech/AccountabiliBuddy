using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The made-up tasks Settings' test rings show (Alerts &amp; on-screen › Task reminders: Ring one, Ring 6, Can't miss). They are never
/// added to the task list (<see cref="TaskReminderService.RingSamples"/>). They borrow the user's first labels for their dots, so the
/// card looks like a real one.
/// </summary>
public static class TaskReminderSamples
{
    private static readonly (string Title, int Minutes)[] Titles =
    [
        ("Test reminder: reply to the project email", 25),
        ("Test reminder: draft the weekly update", 50),
        ("Test reminder: book the dentist", 15),
        ("Test reminder: review the pull request", 25),
        ("Test reminder: water the plants", 5),
        ("Test reminder: plan tomorrow's first task", 15),
    ];

    /// <summary>One plain test reminder.</summary>
    public static IReadOnlyList<TaskItem> One(IReadOnlyList<TaskLabel> labels, DateTimeOffset now)
    {
        return [Make(0, labels, now, cantMiss: false)];
    }

    /// <summary>A stack of six, due together.</summary>
    public static IReadOnlyList<TaskItem> Six(IReadOnlyList<TaskLabel> labels, DateTimeOffset now)
    {
        return Enumerable.Range(0, Titles.Length).Select(index => Make(index, labels, now, cantMiss: false)).ToList();
    }

    /// <summary>One Can't miss reminder (full screen, hold to dismiss).</summary>
    public static IReadOnlyList<TaskItem> CantMiss(IReadOnlyList<TaskLabel> labels, DateTimeOffset now)
    {
        return [Make(1, labels, now, cantMiss: true) with { Title = "Test reminder: the meeting starts soon" }];
    }

    private static TaskItem Make(int index, IReadOnlyList<TaskLabel> labels, DateTimeOffset now, bool cantMiss)
    {
        (string title, int minutes) = Titles[index];
        Guid? label = labels.Count == 0 ? null : labels[index % labels.Count].Id;
        return new TaskItem(Guid.NewGuid(), title, TaskBin.Queued, index, now, now, now, LabelId: label, EstimateMinutes: minutes, CantMiss: cantMiss);
    }
}
