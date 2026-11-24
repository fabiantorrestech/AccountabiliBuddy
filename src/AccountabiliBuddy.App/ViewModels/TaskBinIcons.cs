using AccountabiliBuddy.Core.Domain;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One icon per bin (ADR 32: one meaning, one icon; round 2 brief §3.10): the Shelf's section headers and every Move › menu use
/// these, so To sort, Next and Later look the same wherever they appear.
/// </summary>
public static class TaskBinIcons
{
    public static MaterialIconKind Of(TaskBin bin)
    {
        return bin switch
        {
            TaskBin.Session => MaterialIconKind.PlayCircleOutline,
            TaskBin.Queued => MaterialIconKind.PlaylistPlus,
            TaskBin.Sort => MaterialIconKind.InboxOutline,
            TaskBin.Next => MaterialIconKind.PageLast,
            TaskBin.Later => MaterialIconKind.InboxArrowDownOutline,
            TaskBin.Done => MaterialIconKind.CheckCircleOutline,
            _ => MaterialIconKind.Tray,
        };
    }

    /// <summary>
    /// The Tasks redesign's bin icons (checklist 3.33, SPEC): Today (Queued) CalendarToday, Next SkipNext, To sort InboxOutline, Later
    /// ClockOutline, Done CheckCircleOutline; This session keeps the app's PlayCircleOutline. The bin pill uses them now; the pages move
    /// from <see cref="Of"/> to these when they are restyled (plan items 4, 5, 8), so each page keeps one icon per bin meanwhile.
    /// </summary>
    public static MaterialIconKind Redesigned(TaskBin bin)
    {
        return bin switch
        {
            TaskBin.Session => MaterialIconKind.PlayCircleOutline,
            TaskBin.Queued => MaterialIconKind.CalendarToday,
            TaskBin.Sort => MaterialIconKind.InboxOutline,
            TaskBin.Next => MaterialIconKind.SkipNext,
            TaskBin.Later => MaterialIconKind.ClockOutline,
            TaskBin.Done => MaterialIconKind.CheckCircleOutline,
            _ => MaterialIconKind.Tray,
        };
    }
}
