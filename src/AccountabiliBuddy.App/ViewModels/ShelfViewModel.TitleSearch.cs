namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 9.09: a label or category picked in the title-bar menu's search shows the Shelf with only those labels picked in the label
// row (the view's working copy, as a chip click does; labels hidden on this PC stay as they were).
public sealed partial class ShelfViewModel
{
    /// <summary>The label row's picks become exactly <paramref name="labelIds"/> (No label off).</summary>
    public void ShowOnlyLabels(IReadOnlyCollection<Guid> labelIds)
    {
        _views?.Modify(config => config with { Labels = WithHiddenLabelIds(config.Labels, labelIds), NoLabel = false });
    }
}
