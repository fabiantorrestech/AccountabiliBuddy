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
}
