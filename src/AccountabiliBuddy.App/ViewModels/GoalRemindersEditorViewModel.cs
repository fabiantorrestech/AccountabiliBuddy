using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Edit goal › Reminders and New goal's optional step (round 2 R5.4, brief §4.4 "Edit goal › Reminders", §5.9, §5.10): the heading with
/// the PB badge, "Suggestions only: nothing locks or waits. They ring like task reminders.", the numbered list, "Add a reminder", and the
/// rules "Skip when the goal is already done" / "Nudge when the week falls behind" with the per-mode defaults from Settings › Goals.
/// The list is saved with the dialog through <see cref="GoalsService"/> (R5b2 wires the ringing). Reasons are private text: on screen only.
/// </summary>
public sealed partial class GoalRemindersEditorViewModel : ObservableObject
{
    private readonly List<Guid> _removed = [];
    private DateOnly _today;
    private bool _loading;

    public GoalRemindersEditorViewModel(bool productaBuddy)
    {
        IsProductaBuddy = productaBuddy;
    }

    public event EventHandler? Changed;

    public bool IsProductaBuddy { get; }

    public string Lead => "Suggestions only: nothing locks or waits. They ring like task reminders.";

    public ObservableCollection<GoalReminderEditRow> Rows { get; } = [];

    [ObservableProperty]
    public partial bool SkipWhenDone { get; set; } = true;

    [ObservableProperty]
    public partial bool NudgeWhenBehind { get; set; }

    /// <summary>"Today's reminders stay quiet once you've hit 15 min".</summary>
    [ObservableProperty]
    public partial string SkipNote { get; set; } = "Today's reminders stay quiet once the goal is done.";

    public string NudgeNote => "One nudge the morning after you drop below pace";

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The ids of reminders taken off the list (deleted on save).</summary>
    public IReadOnlyList<Guid> Removed => _removed;

    /// <summary>"4 set", "None", "Optional".</summary>
    public string Summary => Rows.Count == 0 ? "None" : $"{Rows.Count} set";

    /// <summary>The first row that can't be saved, or null.</summary>
    public string? Problem => Rows.Select(row => row.Problem).FirstOrDefault(problem => problem is not null);

    /// <summary>Shows a goal's reminders (a new goal: none, with the Settings › Goals defaults for the rules).</summary>
    public void Load(IEnumerable<GoalReminder> reminders, GoalReminderDefaults defaults, DateOnly today, string? doneText)
    {
        _loading = true;
        try
        {
            _today = today;
            _removed.Clear();
            Rows.Clear();
            List<GoalReminder> list = reminders.OrderBy(reminder => reminder.Position).ToList();
            foreach (GoalReminder reminder in list)
            {
                Rows.Add(NewRow(reminder, startEditing: false));
            }

            // The rules are the goal's: every reminder carries them; with none yet, the defaults for the mode the app is in.
            SkipWhenDone = list.Count > 0 ? list.All(reminder => reminder.SkipWhenDone) : defaults.SkipWhenDone;
            NudgeWhenBehind = list.Count > 0 ? list.Any(reminder => reminder.NudgeWhenBehind) : defaults.NudgeFor(IsProductaBuddy);
            SkipNote = doneText is null ? "Today's reminders stay quiet once the goal is done." : $"Today's reminders stay quiet once you've hit {doneText}.";
            Renumber();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The reminders to save for <paramref name="goalId"/>, in list order with the rule switches.</summary>
    public IReadOnlyList<GoalReminder> Build(Guid goalId)
    {
        return Rows.Select((row, index) => row.ToReminder(index + 1, SkipWhenDone, NudgeWhenBehind) with { GoalId = goalId }).ToList();
    }

    [RelayCommand]
    private void AddReminder()
    {
        var reminder = new GoalReminder(Guid.NewGuid(), Guid.Empty, GoalReminderDays.EveryDay, new TimeOnly(20, 0), Rows.Count + 1, default,
            SkipWhenDone: SkipWhenDone, NudgeWhenBehind: NudgeWhenBehind);
        Rows.Add(NewRow(reminder, startEditing: true));
        Renumber();
        Raise();
    }

    partial void OnSkipWhenDoneChanged(bool value) => Raise();

    partial void OnNudgeWhenBehindChanged(bool value) => Raise();

    private GoalReminderEditRow NewRow(GoalReminder reminder, bool startEditing)
    {
        return new GoalReminderEditRow(reminder, _today, startEditing, Remove, Raise);
    }

    private void Remove(GoalReminderEditRow row)
    {
        Rows.Remove(row);
        _removed.Add(row.Id);
        Renumber();
        Raise();
    }

    private void Renumber()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].Number = i + 1;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
    }

    private void Raise()
    {
        if (_loading)
        {
            return;
        }

        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
