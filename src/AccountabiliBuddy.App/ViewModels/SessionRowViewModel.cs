namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One session in the History list.</summary>
/// <param name="Reason">The early-end reason, if the session was ended early.</param>
/// <param name="StartedAt">Identifies the session (for omitting it).</param>
/// <param name="IsOmitted">Omitted: shown greyed, left out of totals and the day bar.</param>
public sealed record SessionRowViewModel(
    string WhenText,
    string Title,
    string LengthText,
    string OutcomeText,
    string? Reason,
    DateTimeOffset StartedAt,
    bool IsOmitted)
{
    public bool HasReason => Reason is not null;

    public string ReasonText => Reason is null ? string.Empty : $"“{Reason}”";

    public double RowOpacity => IsOmitted ? 0.45 : 1;

    public string MarkButtonText => IsOmitted ? "Include" : "Omit";

    public string MarkButtonTip => IsOmitted
        ? "Count this session again"
        : "Leave it out of the day bar and totals (e.g. a test); it stays listed";
}
