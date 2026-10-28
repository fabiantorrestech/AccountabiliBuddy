using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the Plan timeline: when, a 6px bar (sleep, wake and bedtime in the sleep colour), a title and a Muted line; a work part also lists its sub-rows.</summary>
public sealed partial class PlanTimelineRowViewModel
{
    private readonly Action<PlanTimelineRowViewModel> _open;

    public PlanTimelineRowViewModel(Action<PlanTimelineRowViewModel> open)
    {
        _open = open;
    }

    public string TimeText { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string SubText { get; init; } = string.Empty;

    /// <summary>Sleep, wake and bedtime rows use the sleep colour; the others the plan colour.</summary>
    public bool IsSleep { get; init; }

    public IReadOnlyList<PlanSubRowViewModel> SubRows { get; init; } = Array.Empty<PlanSubRowViewModel>();

    public bool HasSubRows => SubRows.Count > 0;

    /// <summary>The plan part that stands for this row, so its editor can open on it; null for a part the editor doesn't list (a Budget day's window).</summary>
    public Guid? PartId { get; init; }

    [RelayCommand]
    private void Open() => _open(this);
}
