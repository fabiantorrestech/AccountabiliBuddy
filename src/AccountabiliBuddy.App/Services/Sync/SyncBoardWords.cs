using System.Globalization;
using AccountabiliBuddy.Core.Sync;
using Material.Icons;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The words and the icon for a board dot (SYNC1b): colour is never the only signal, so each colour has its icon (green a check, yellow a
/// clock, red an alert, grey a dash; the phone uses the same icons) and every reason has a plain sentence. Times are 12-hour with AM/PM.
/// </summary>
public static class SyncBoardWords
{
    /// <summary>The icon of a colour (the same meaning, the same icon on the phone).</summary>
    public static MaterialIconKind IconOf(SyncDotColor color) => color switch
    {
        SyncDotColor.Green => MaterialIconKind.CheckCircle,
        SyncDotColor.Yellow => MaterialIconKind.ClockOutline,
        SyncDotColor.Red => MaterialIconKind.AlertCircle,
        _ => MaterialIconKind.MinusCircleOutline,
    };

    /// <summary>The colour's own word, for the accessible name.</summary>
    public static string ColorWord(SyncDotColor color) => color switch
    {
        SyncDotColor.Green => "Received",
        SyncDotColor.Yellow => "Waiting",
        SyncDotColor.Red => "Problem",
        _ => "Not applicable",
    };

    /// <summary>The sentence for a dot's reason.</summary>
    public static string Words(SyncDot dot) => dot.Reason switch
    {
        SyncDotReason.Confirmed => "Received your newest file",
        SyncDotReason.CaughtUp => "Up to date as of its last file",
        SyncDotReason.Waiting => "Waiting for its next file to confirm",
        SyncDotReason.NoFileYet => "No file from it yet",
        SyncDotReason.OtherMethod => "Confirmed, but through another way",
        SyncDotReason.OlderApp => "Older app: it says what it holds, not how or which parts",
        SyncDotReason.Held => "Held back: a session, a wait or a question",
        SyncDotReason.NotConfirmed => "Wrote since, but didn't confirm your newest file",
        SyncDotReason.Failed => "Couldn't read your file",
        SyncDotReason.Refused => "Refused your file",
        SyncDotReason.MethodQuiet => "Nothing has come from it by this way for a while",
        SyncDotReason.NoReport => "No report on this yet",
        SyncDotReason.NotTaken => "Not taken: switched off there or not in this file",
        SyncDotReason.Watch => "A watch: reached through its phone",
        SyncDotReason.Removed => "Removed from the group",
        SyncDotReason.Silent => "Not heard from for over 30 days",
        SyncDotReason.NotSent => "Your newest file isn't on this way yet",
        SyncDotReason.MethodNotUsed => "Not used by that device",
        _ => "No other devices yet",
    };

    /// <summary>The name of a part code.</summary>
    public static string PartName(string code) => code switch
    {
        "tasks" => "Tasks, labels and views",
        "goals" => "Goals",
        "goalHistory" => "Goal history",
        "sessions" => "Sessions",
        "dayPlan" => "Day plan",
        "settings" => "Settings",
        "steps" => "Steps",
        "lunch" => "Lunch",
        "oldLists" => "Older task lists",
        "actions" => "Phone actions",
        _ => "Other",
    };

    /// <summary>The sentence for a failure or leftover code of an unreadable file.</summary>
    public static string ReasonOfFile(string reason) => reason switch
    {
        "damaged" => "Damaged, or cut off",
        "tooBig" => "Too big to be a sync file",
        "newerFormat" => "From a newer version of the app",
        SyncUnreadableFile.OtherGroup => "Another group's leftover (from before a device was removed)",
        SyncUnreadableFile.OwnCopy => "An extra copy of this PC's own file",
        _ => "Can't be read",
    };

    /// <summary>12-hour time of day ("4:05 PM"); a day before today adds the date ("Nov 12, 4:05 PM").</summary>
    public static string Clock(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(time, zone);
        DateTimeOffset today = TimeZoneInfo.ConvertTime(now, zone);
        string clock = local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return local.Date == today.Date ? clock : local.ToString("MMM d", CultureInfo.InvariantCulture) + ", " + clock;
    }
}
