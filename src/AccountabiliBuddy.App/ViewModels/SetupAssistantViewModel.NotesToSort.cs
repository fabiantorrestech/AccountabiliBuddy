namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's "Come-backs and session notes are now in To sort" page (ADR 94; a new feature for upgraded installs only): information
/// only, the notes were already turned into To sort tasks by themselves. Got it counts as answered on this PC, so it isn't offered again.
/// </summary>
public sealed partial class SetupAssistantViewModel
{
    public bool IsNotesToSort => Step == AssistantStep.NotesToSort;

    private async Task FinishNotesToSortStepAsync()
    {
        if (SkipFeature is not null)
        {
            await SkipFeature(Services.Setup.SetupFeatures.NotesToSortId);
        }
    }
}
