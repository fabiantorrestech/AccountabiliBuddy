using AccountabiliBuddy.Core.Domain;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the bin pill's menu (Tasks redesign checklist 3.68-3.73, resolution O4): a bin, its name and icon, a hint at the right
/// ("Queued" on Today, "Default" on To sort for a new task) and whether it is the task's bin now.
/// </summary>
public sealed record BinPillChoice(TaskBin Bin, string Name, MaterialIconKind Icon, string Hint, bool IsCurrent)
{
    public bool HasHint => Hint.Length > 0;

    /// <summary>
    /// The menu for a task in <paramref name="current"/>: This session (an existing task only, O4; the caller moves it through the session's
    /// cap), Today (= Queued), Next, Later, To sort.
    /// </summary>
    public static IReadOnlyList<BinPillChoice> For(TaskBin current, bool isNewTask)
    {
        var bins = new List<TaskBin>();
        if (!isNewTask)
        {
            bins.Add(TaskBin.Session);
        }

        bins.AddRange([TaskBin.Queued, TaskBin.Next, TaskBin.Later, TaskBin.Sort]);
        return bins.Select(bin => new BinPillChoice(bin, NameOf(bin), TaskBinIcons.Redesigned(bin), HintOf(bin, isNewTask), bin == current)).ToList();
    }

    /// <summary>The pill's name for a bin: Queued is "Today", Finished is "Done"; the rest as the app names them.</summary>
    public static string NameOf(TaskBin bin)
    {
        return bin switch
        {
            TaskBin.Queued => "Today",
            TaskBin.Done => "Done",
            _ => Core.Tasks.TaskBins.Name(bin),
        };
    }

    private static string HintOf(TaskBin bin, bool isNewTask)
    {
        return bin switch
        {
            TaskBin.Queued => "Queued",
            TaskBin.Sort when isNewTask => "Default",
            _ => string.Empty,
        };
    }
}
