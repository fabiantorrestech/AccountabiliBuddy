using AccountabiliBuddy.Core.Import;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One source on the assistant's "Bring your blocks" page: a blocker found on this PC, a file, or a paste.</summary>
public sealed partial class DetectedSourceRowViewModel : ObservableObject
{
    public DetectedSourceRowViewModel(DetectedImport detected)
    {
        Detected = detected;
        IsSelected = detected.HasLists;
    }

    public DetectedImport Detected { get; }

    public string Title => Detected.Title;

    public string Summary => Detected.Summary;

    public string? Problem => Detected.Problem;

    public bool HasProblem => Problem is not null;

    public bool CanSelect => Detected.HasLists;

    /// <summary>The lists in it with what they contain, e.g. "Distractions: 75 sites, started by hand"; empty when
    /// there is only one (the summary already says it).</summary>
    public string Details => Detected.Lists.Count(list => !list.IsEmpty) <= 1
        ? string.Empty
        : string.Join("\n", Detected.Lists.Where(list => !list.IsEmpty).Select(Describe));

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    private static string Describe(ImportedBlockList list)
    {
        int entries = list.BlockEntries.Count;
        string when = list.Schedule.Count == 0 ? "started by hand" : $"{list.Schedule.Count} scheduled time(s) a week";
        return $"• {list.Name}: {entries} site(s)/app(s), {when}";
    }
}
