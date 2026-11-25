namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of Move and label's change table (checklist 8c.39, 8c.40): a task that will change, with its bin, label and tags before → after
/// ("To sort → Later", bold) or as they stay ("To sort (stays)", dim).
/// </summary>
public sealed record MoveAndLabelChangeRow(
    string Title, string BinText, bool BinChanges, string LabelText, bool LabelChanges, string TagsText, bool TagsChange);
