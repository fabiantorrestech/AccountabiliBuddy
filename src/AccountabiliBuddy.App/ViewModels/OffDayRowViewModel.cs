namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One planned off day in the Schedule tab.</summary>
public sealed record OffDayRowViewModel(DateOnly Date)
{
    public string Text => Date.ToString("dddd d MMMM yyyy");
}
