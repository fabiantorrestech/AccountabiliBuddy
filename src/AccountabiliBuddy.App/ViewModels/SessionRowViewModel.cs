namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One session in the History list.</summary>
/// <param name="Reason">The early-end reason, if the session was ended early.</param>
/// <param name="StartedAt">Identifies the session (for omitting it).</param>
/// <param name="IsOmitted">Omitted: shown greyed, left out of totals and the day bar.</param>
/// <param name="ConfirmingDelete">An added session whose Delete was clicked: the row asks before deleting.</param>
/// <param name="DeviceName">Another device's session (ADR 22): the device it was counted on, shown as a tag.</param>
public sealed record SessionRowViewModel(
    string WhenText,
    string Title,
    string LengthText,
    string OutcomeText,
    string? Reason,
    DateTimeOffset StartedAt,
    bool IsOmitted,
    bool IsAdded = false,
    DateTimeOffset? EndedAt = null,
    bool ConfirmingDelete = false,
    string? DeviceName = null)
{
    /// <summary>Counted on another device: shown with its tag; omitted, edited or deleted only there.</summary>
    public bool IsReceived => DeviceName is not null;

    /// <summary>Added by hand (ADR 20): edited or deleted here instead of omitted. Another device's: neither.</summary>
    public bool ShowsMarkButton => !IsAdded && !IsReceived;

    /// <summary>Edit and Delete show for an added session until Delete asks to be confirmed.</summary>
    public bool ShowsAddedButtons => IsAdded && !ConfirmingDelete;

    public bool HasReason => Reason is not null;

    public string ReasonText => Reason is null ? string.Empty : $"“{Reason}”";

    public double RowOpacity => IsOmitted ? 0.45 : 1;

    public string MarkButtonText => IsOmitted ? "Include" : "Omit";

    public string MarkButtonTip => IsOmitted
        ? "Count this session again"
        : "Leave it out of the day bar and totals (e.g. a test); it stays listed";
}
