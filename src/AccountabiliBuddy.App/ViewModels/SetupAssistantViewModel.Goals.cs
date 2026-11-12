using AccountabiliBuddy.App.Services.Goals;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's Goals page (round 2 R5, Q440; a new feature for existing installs only): what Goals are, that Deep work is built
/// in, and the day the weekly goals review comes (Settings › Goals' review day, saved on Next; picking it is what "set up" means).
/// </summary>
public sealed partial class SetupAssistantViewModel
{
    /// <summary>The goals (Settings › Goals' review day); null when they can't be read here (the page then only explains).</summary>
    public GoalsService? Goals { get; init; }

    public bool IsGoalsIntro => Step == AssistantStep.GoalsIntro;

    /// <summary>"Sunday" … "Saturday", in <see cref="DayOfWeek"/> order.</summary>
    public IReadOnlyList<string> ReviewDayChoices { get; } = Enum.GetValues<DayOfWeek>().Select(day => day.ToString()).ToList();

    /// <summary>The review day picked on the page (index into <see cref="ReviewDayChoices"/>).</summary>
    [ObservableProperty]
    public partial int ReviewDayIndex { get; set; }

    private void OnGoalsStepShown(AssistantStep step)
    {
        if (step == AssistantStep.GoalsIntro && Goals is { Store.IsReady: true } goals)
        {
            ReviewDayIndex = (int)goals.Store.PageSettings.ReviewDay;
        }
    }

    // Next on the Goals page: the review day picked becomes Settings › Goals' (the page's "set up" check reads it). Display and timing
    // only, so it applies at once in both modes.
    private async Task FinishGoalsStepAsync()
    {
        if (Goals is not { Store.IsReady: true } goals || ReviewDayIndex < 0 || ReviewDayIndex > 6)
        {
            return;
        }

        GoalPageSettings settings = goals.Store.PageSettings;
        GoalReviewSettings review = settings.ReviewSettings with { Day = (DayOfWeek)ReviewDayIndex };
        await goals.SetPageSettingsAsync(settings with { Review = review });
    }
}
