using AccountabiliBuddy.App.Services;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One deletion in History's "Deleted" list, with Restore.</summary>
public sealed record DeletedRowViewModel(DeletedUsage Deletion)
{
    public string Text => $"{Deletion.Label} · {Deletion.PeriodLabel}";
}
