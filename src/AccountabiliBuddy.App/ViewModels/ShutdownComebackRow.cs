using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>An open come-back on the shutdown card: ticked here when it's done.</summary>
public sealed partial class ShutdownComebackRow(Guid id, string text) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}
