namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of Today's "Next up" card: when it starts ("Now" while it is on) and what it is.</summary>
/// <param name="TimeText">E.g. "3:00 PM", or "Now".</param>
/// <param name="Title">E.g. "Deep work until 5:00 PM".</param>
/// <param name="PlannedText">The tasks put in the plan for this part ("Put in plan", ADR 50), e.g. "Draft report · Reply to Dana"; empty with none.</param>
public sealed record NextUpRow(string TimeText, string Title, string PlannedText = "")
{
    public bool HasPlanned => PlannedText.Length > 0;
}
