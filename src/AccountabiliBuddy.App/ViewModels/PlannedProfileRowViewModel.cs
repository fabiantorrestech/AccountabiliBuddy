using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Import;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A profile on the assistant's review page: rename it or untick it to leave it (and its times) out.</summary>
public sealed partial class PlannedProfileRowViewModel : ObservableObject
{
    public PlannedProfileRowViewModel(PlannedProfile planned, int scheduledBlocks)
    {
        Planned = planned;
        Name = planned.Profile.Name;
        IsIncluded = true;

        int blocks = planned.Profile.Rules.Count(rule => rule.Verdict == RuleVerdict.Block);
        int allows = planned.Profile.Rules.Count(rule => rule.Verdict == RuleVerdict.Allow
                                                         && rule.Note is not (ImportPlanner.AllowOtherAppsNote or ImportPlanner.AllowOtherSitesNote));
        string when = scheduledBlocks == 0 ? "start it yourself" : $"{scheduledBlocks} weekly block(s)";
        string unlock = planned.Profile.UnlockOptions.Count == 0
            ? "no unlock option"
            : string.Join(", ", planned.Profile.UnlockOptions.Select(option => $"unlock after {option.Wait.TotalMinutes:0} min"));
        Details = $"Blocks {blocks} site(s)/app(s)" + (allows > 0 ? $", allows {allows}" : string.Empty) + $" · {when} · {unlock}";
        Origin = planned.IsCombined ? $"Where {string.Join(" and ", planned.SourceNames)} overlap" : null;
        Skipped = planned.SkippedLines.Count == 0
            ? null
            : $"Couldn't read {planned.SkippedLines.Count} entr{(planned.SkippedLines.Count == 1 ? "y" : "ies")}: {string.Join(", ", planned.SkippedLines.Take(4))}";
    }

    public PlannedProfile Planned { get; }

    public string Details { get; }

    public string? Origin { get; }

    public bool IsCombined => Origin is not null;

    public string? Skipped { get; }

    public bool HasSkipped => Skipped is not null;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsIncluded { get; set; }
}
