using System.Globalization;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Tasks redesign's count rule for actions on a selection (checklist 3.77-3.79): an action on 2 or more items ends with " (N)" ("Delete (3)",
/// "Apply (2)"); on one item it has no count ("Delete"). Only for action labels (buttons, menu entries): headings and sentences say it in words
/// ("3 selected", "Delete 3 tasks?"), and counts that are not actions (badges, column counts, toasts) stay as they are.
/// </summary>
public static class SelectionCountText
{
    /// <summary>"<paramref name="action"/> (<paramref name="count"/>)" for 2 or more, else <paramref name="action"/> alone.</summary>
    public static string Action(string action, int count)
    {
        return count >= 2 ? $"{action} ({count.ToString(CultureInfo.CurrentCulture)})" : action;
    }
}
