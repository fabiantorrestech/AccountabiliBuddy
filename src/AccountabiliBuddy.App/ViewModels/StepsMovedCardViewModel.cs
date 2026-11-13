using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.App.Services.Today;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The one-time quiet card "Steps now live in Goals" (TD1, owner 2026-11-13). Daily steps left Today; steps are seen and tracked through a
/// Steps goal. The card shows once on a PC where steps were on Today (<see cref="StepsMovedNote.Show"/>, decided when the saved layout
/// was first read), a phone still shares steps, the old card wasn't hidden, and there is no steps goal. "Add a steps goal" makes one
/// (Steps, 8,000 a day, Show on Today on); × hides it for good. Nothing is ever created on its own. It feeds no guard.
/// </summary>
public sealed partial class StepsMovedCardViewModel : ObservableObject
{
    /// <summary>The target the card's goal is made with (the brief's 8,000 a day).</summary>
    public const double DailyTarget = 8000;

    private readonly TodayLayoutService _layout;
    private readonly DailyStepsService _steps;
    private readonly GoalsService _goals;

    public StepsMovedCardViewModel(TodayLayoutService layout, DailyStepsService steps, GoalsService goals)
    {
        _layout = layout;
        _steps = steps;
        _goals = goals;
        _layout.Changed += (_, _) => Refresh();
        _steps.Changed += (_, _) => Refresh();
        _goals.Store.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The card is drawn.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Whether a live goal already counts steps (a Steps goal made here, on the phone, or by hand).</summary>
    public static bool HasStepsGoal(GoalLists lists) =>
        lists.LiveGoals.Any(goal => goal.Target.Unit == GoalUnit.Steps || goal.Sources.Any(rule => rule.Type == GoalSourceType.HealthSteps));

    public void Refresh()
    {
        if (_layout.Current.StepsMovedNote != StepsMovedNote.Show)
        {
            IsVisible = false;
            return;
        }

        if (!_goals.Store.IsReady)
        {
            IsVisible = false;
            return;
        }

        if (HasStepsGoal(_goals.Store.Lists))
        {
            // Steps are already tracked in Goals: nothing to point at, now or later.
            IsVisible = false;
            _ = _layout.FinishStepsMovedNoteAsync();
            return;
        }

        IsVisible = _steps.Current is not null && !_steps.Hidden;
    }

    /// <summary>One tap: a Steps goal, 8,000 a day, Show on Today on, in the first area.</summary>
    [RelayCommand]
    private async Task AddGoalAsync()
    {
        GoalArea? area = _goals.Store.Lists.LiveAreas.FirstOrDefault();
        if (area is null || !_goals.Store.IsReady || HasStepsGoal(_goals.Store.Lists))
        {
            return;
        }

        double position = _goals.Store.Lists.LiveGoals.Where(goal => goal.AreaId == area.Id).Select(goal => goal.Position).DefaultIfEmpty(0).Max() + 1;
        var goal = new Goal(
            Guid.NewGuid(), area.Id, "Steps", "steps", "#0D9488", GoalKind.Amount,
            new GoalTarget(GoalPeriod.Day, DailyTarget, GoalUnit.Steps),
            [new GoalSourceRule(Guid.NewGuid(), GoalSourceType.HealthSteps)], default, position, default, ShowOnToday: true);
        if (await _goals.AddGoalAsync(goal, this) == GoalChangeResult.Applied)
        {
            await _layout.FinishStepsMovedNoteAsync();
        }
    }

    /// <summary>× : never show the card again.</summary>
    [RelayCommand]
    private Task DismissAsync() => _layout.FinishStepsMovedNoteAsync();
}
