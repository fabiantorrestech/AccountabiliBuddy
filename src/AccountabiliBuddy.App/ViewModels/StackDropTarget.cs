namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What kind of place a dragged stack of tasks is over (Tasks redesign 8b, SPEC "Dragging").</summary>
public enum StackDropKind
{
    /// <summary>A Shelf column or its rail: a bin (Group by Bins) or a label (Group by Labels).</summary>
    Column,

    /// <summary>A label: its chip in the label row or its row in the +N list.</summary>
    Label,

    /// <summary>The +N list's "No label" row.</summary>
    NoLabel,

    /// <summary>The +N chip (holding the stack on it opens the full label list).</summary>
    More,

    /// <summary>The Today pill: Today › Queued, never This session.</summary>
    Today,

    /// <summary>The drag bar's left zone: "choose a bin and label" (Group by None: the Archive zone).</summary>
    Choose,

    /// <summary>The drag bar's Cancel zone: nothing changes, the selection stays.</summary>
    Cancel,

    /// <summary>A page arrow (‹ ›): held, it turns the page; letting go on it drops nothing.</summary>
    PageArrow,
}

/// <summary>
/// The place under a dragged stack. <paramref name="Column"/> is set for <see cref="StackDropKind.Column"/>; <paramref name="Row"/> and
/// <paramref name="After"/> say where in its own column the stack would go (a reorder, 8b.35), null for the end. <paramref name="LabelId"/>
/// and <paramref name="LabelName"/> (the label's full name) are set for <see cref="StackDropKind.Label"/>; <paramref name="PageStep"/> (-1 or 1)
/// for <see cref="StackDropKind.PageArrow"/>.
/// </summary>
public sealed record StackDropTarget(
    StackDropKind Kind,
    TaskSectionViewModel? Column = null,
    TaskListRowViewModel? Row = null,
    bool After = false,
    Guid? LabelId = null,
    string? LabelName = null,
    int PageStep = 0)
{
    /// <summary>Over this place the stack shrinks to 92% (8b.08); over "choose a bin and label" it stays full size.</summary>
    public bool ShrinksStack => Kind != StackDropKind.Choose;
}
