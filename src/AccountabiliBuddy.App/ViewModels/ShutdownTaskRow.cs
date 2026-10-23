using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A task left open today, on the shutdown card: carried into tomorrow's checklist unless unticked.</summary>
public sealed partial class ShutdownTaskRow(string title) : ObservableObject
{
    public string Title { get; } = title;

    [ObservableProperty]
    public partial bool Carry { get; set; } = true;
}
