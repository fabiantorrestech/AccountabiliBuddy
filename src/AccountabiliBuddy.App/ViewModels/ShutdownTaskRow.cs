using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A task left open today, on the shutdown card: carried into tomorrow's checklist unless ticked (done).</summary>
/// <param name="categoryId">Its category, carried with it while that category still exists; null = no category.</param>
public sealed partial class ShutdownTaskRow(string title, Guid? categoryId = null) : ObservableObject
{
    public string Title { get; } = title;

    public Guid? CategoryId { get; } = categoryId;

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}
