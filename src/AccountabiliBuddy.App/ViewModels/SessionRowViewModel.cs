namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One session in the History list.</summary>
/// <param name="Reason">The early-end reason, if the session was ended early.</param>
/// <param name="StartedAt">Identifies the session (for marking it as not real).</param>
/// <param name="IsNotReal">Marked as not real: shown greyed, left out of totals and the day bar.</param>
public sealed record SessionRowViewModel(
    string WhenText,
    string Title,
    string LengthText,
    string OutcomeText,
    string? Reason,
    DateTimeOffset StartedAt,
    bool IsNotReal)
{
    public bool HasReason => Reason is not null;

    public string ReasonText => Reason is null ? string.Empty : $"“{Reason}”";

    public double RowOpacity => IsNotReal ? 0.45 : 1;

    public string MarkButtonText => IsNotReal ? "Undo: count it" : "Not real";

    public string MarkButtonTip => IsNotReal
        ? "Count this session again"
        : "A test or a try-out: keep it listed but leave it out of the day bar and totals";
}
