using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Stats for one goal (round 2 R5.4; brief §4.4 "the Stats button opens it filtered to the goal", §5.4 and change note §4a "Stats shows
/// paused stretches as their own state (hatched, "Paused Sep 14 – Oct 30")"): opened from the Goals header's Stats with a goal shown.
/// The goal's year as the goals heatmap draws it (paused days hatched, neither hit nor missed), its paused stretches in words, its age
/// and streak. Stats' own views stay as they are underneath; × goes back to them. Goal names are private text: on screen only.
/// </summary>
public sealed partial class StatsGoalHistoryViewModel : ObservableObject
{
    public StatsGoalHistoryViewModel(GoalFacts facts, GoalSourceData data, GoalPageSettings settings, string? areaName)
    {
        Goal goal = facts.Goal;
        GoalId = goal.Id;
        Name = goal.Name;
        Icon = GoalIcons.Of(goal.Icon);
        ColourBrush = GoalIcons.Brush(goal.Color);
        DateOnly today = facts.Today;
        string area = areaName is { Length: > 0 } ? $"{areaName} · " : string.Empty;
        Subtitle = $"{area}{GoalTexts.KindText(goal)} · {GoalTexts.TargetLine(goal, today)} · {facts.Age.Short} going";
        AgeText = facts.Age.Tooltip;
        StreakText = facts.Streak switch
        {
            0 => "No streak right now",
            1 => "Streak: 1",
            int streak => $"Streak: {streak}",
        };
        PausedStretches = PausedTexts(goal, today);
        Heatmap = new GoalHeatmapViewModel { WantedRange = GoalHeatmapRange.Year, MonthLines = settings.MonthLines };
        Heatmap.Show(goal, data, today, settings.WeekStart, settings.HeatmapLabels, sameGoal: false);
    }

    /// <summary>× : back to Stats' own views.</summary>
    public event EventHandler? CloseRequested;

    public Guid GoalId { get; }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    public IBrush ColourBrush { get; }

    /// <summary>"Learning · daily habit · 15 min a day · 6 months going".</summary>
    public string Subtitle { get; }

    /// <summary>"Since Mon 5/4/2026 · 191 days (27 weeks) · paused 46 days".</summary>
    public string AgeText { get; }

    public string StreakText { get; }

    /// <summary>"Paused Sep 14 – Oct 30", "Paused since Mon 11/2" (oldest first); empty when never paused.</summary>
    public IReadOnlyList<string> PausedStretches { get; }

    public bool HasPausedStretches => PausedStretches.Count > 0;

    public string PausedNote => "Paused days are hatched: neither hit nor missed, so streaks and pace aren't punished.";

    public GoalHeatmapViewModel Heatmap { get; }

    /// <summary>"Paused Sep 14 – Oct 30" for each stretch (the day before it was turned back on is its last).</summary>
    public static IReadOnlyList<string> PausedTexts(Goal goal, DateOnly today)
    {
        var texts = new List<string>();
        foreach (GoalPauseStretch stretch in (goal.Pauses ?? []).OrderBy(item => item.From))
        {
            if (stretch.ResumedOn is DateOnly resumed)
            {
                DateOnly last = resumed.AddDays(-1);
                texts.Add(last < stretch.From
                    ? $"Paused {Short(stretch.From, today)}"
                    : $"Paused {Short(stretch.From, today)} – {Short(last, today)}");
            }
            else
            {
                texts.Add($"Paused since {GoalTexts.Date(stretch.From, today)}");
            }
        }

        return texts;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // "Sep 14", or "Sep 14, 2025" in another year.
    private static string Short(DateOnly day, DateOnly today)
    {
        string text = day.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
        return day.Year == today.Year ? text : $"{text}, {day.Year}";
    }
}
