using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A task left open today, on the shutdown card: carried into tomorrow's checklist unless ticked (done).</summary>
/// <param name="categoryId">Its category, carried with it while that category still exists; null = no category.</param>
/// <param name="hiddenCategoryId">Its category when this device doesn't show that label (ADR 89); never shown, but carried with the task.</param>
/// <param name="hiddenTags">Its tags this device doesn't show (ADR 89); never shown, but carried with the task.</param>
public sealed partial class ShutdownTaskRow(string title, Guid? categoryId = null, Guid? hiddenCategoryId = null, IReadOnlyList<string>? hiddenTags = null, Guid? taskId = null) : ObservableObject
{
    /// <summary>The checklist task of the ended session this row stands for (GS5b: carrying it takes its held copy's goal link, due date and reminders); null for a row made by hand.</summary>
    public Guid? TaskId { get; } = taskId;

    public string Title { get; } = title;

    public Guid? CategoryId { get; } = categoryId;

    public Guid? HiddenCategoryId { get; } = hiddenCategoryId;

    public IReadOnlyList<string>? HiddenTags { get; } = hiddenTags;

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}
