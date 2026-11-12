using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the detail's Reminders card (board Goals-PC): its number, a day pill ("Today", "Sun", "Fri 12/5", "Every day"), a time
/// pill ("8:00 PM"), the repeat icon (repeat, or a calendar for once) and the reason ("if not done yet"; private text, shown on screen
/// only, never logged).
/// </summary>
public sealed record GoalReminderRow(int Number, string DayText, string TimeText, MaterialIconKind RepeatIcon, string RepeatTip, string Reason)
{
    public string NumberText => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
