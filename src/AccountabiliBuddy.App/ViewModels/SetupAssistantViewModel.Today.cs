using AccountabiliBuddy.App.Services.Today;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's Today page (TD1; a new feature for existing installs only): every Today part can be hidden, More today has a
/// Show / Hide choice, and steps moved to Goals. Next counts as having seen the steps note (the same as the x on the Today card); the
/// goal itself is only ever made by tapping "Add a steps goal", here or on Today.
/// </summary>
public sealed partial class SetupAssistantViewModel
{
    /// <summary>Today's layout (the page saves nothing but the steps note); null where it isn't available.</summary>
    public TodayLayoutService? TodayLayout { get; init; }

    /// <summary>The one-time steps card, for its "Add a steps goal" button; null where there are no goals.</summary>
    public StepsMovedCardViewModel? StepsMoved { get; init; }

    public bool IsTodayChoices => Step == AssistantStep.TodayChoices;

    private async Task FinishTodayChoicesStepAsync()
    {
        if (TodayLayout is { } layout)
        {
            await layout.FinishStepsMovedNoteAsync();
        }
    }
}
