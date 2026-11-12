namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What the reminder cards ask of whoever rings them (<see cref="Services.Tasks.TaskReminderRinger"/>): each action works on the card
/// shown now. Only Dismiss waits (dismiss protection; Can't miss also needs a hold); Snooze and Start work at once.
/// </summary>
public interface IReminderStackHost
{
    /// <summary>The accent button: Start session / Work on it now, or Add to Queued on the snoozed-3-times nudge.</summary>
    void Primary();

    void SnoozeFor(int minutes);

    /// <summary>One of the three snooze shortcuts (Tonight 6:00 PM, Tomorrow 9:00 AM, Monday 9:00 AM by default).</summary>
    void SnoozeShortcut(int index);

    /// <summary>"Pick a time…": the Snooze until… dialog.</summary>
    void PickTime();

    void Dismiss();

    /// <summary>‹ and ›: the previous or next card of the stack.</summary>
    void Move(int delta);

    /// <summary>"All 6" (true) and the list's back arrow (false).</summary>
    void ShowList(bool list);

    /// <summary>A row of the list: that reminder's card.</summary>
    void Open(int index);

    void SnoozeAll();

    void DismissAll();
}
