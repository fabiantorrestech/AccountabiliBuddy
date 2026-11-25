namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One tag on a Move and label row's Tags line (checklist 8c.17, 8c.20): "#waiting" as it is, struck through in Drift when it is coming
/// off, or a lit "+#quick" pill when it is going on.
/// </summary>
/// <param name="Text">"#waiting" or "+#quick".</param>
/// <param name="IsComingOff">A tag the task has that the picks take off.</param>
/// <param name="IsGoingOn">A tag the picks put on (the lit pill).</param>
public sealed record MoveAndLabelRowTag(string Text, bool IsComingOff, bool IsGoingOn)
{
    public bool IsPlain => !IsGoingOn;
}
