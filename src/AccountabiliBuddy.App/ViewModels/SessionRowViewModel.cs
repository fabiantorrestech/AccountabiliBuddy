namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One session in the History list.</summary>
/// <param name="Reason">The early-end reason, if the session was ended early.</param>
public sealed record SessionRowViewModel(string WhenText, string Title, string LengthText, string OutcomeText, string? Reason)
{
    public bool HasReason => Reason is not null;

    public string ReasonText => Reason is null ? string.Empty : $"“{Reason}”";
}
