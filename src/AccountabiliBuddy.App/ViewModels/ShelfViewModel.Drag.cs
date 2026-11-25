using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 8b (SPEC "Dragging", "Turning the page while dragging"): what the Shelf's own views show while a stack of selected tasks is
// dragged. The drag itself is the page's (TasksViewModel.Drag, Views/TaskStackDrag); these flags only light the Shelf's parts.
public sealed partial class ShelfViewModel
{
    /// <summary>
    /// A stack is being dragged on the Tasks page: the Labels view's ‹ › grow to 34 px with an accent edge (8b.39), "Hold on an arrow to turn
    /// the page" shows left of them (8b.38), and the label row's chips show at full strength (8b.25).
    /// </summary>
    [ObservableProperty]
    public partial bool IsStackDragging { get; set; }

    /// <summary>The stack is over the +N menu's "No label" row (8b.27, 8b.36).</summary>
    [ObservableProperty]
    public partial bool IsNoLabelDropHot { get; set; }

    /// <summary>The stack is over the +N chip: it lights (and opens the full label list, 8b.27).</summary>
    [ObservableProperty]
    public partial bool IsMoreDropHot { get; set; }

    /// <summary>The view sorts by "Your order" (the manual order a drop on a task's own column changes, 8b.35).</summary>
    public bool IsYourOrder => (_views?.Working.Sort ?? TaskViewConfig.SortDefault) == TaskViewConfig.SortDefault;

    /// <summary>The label chip of <paramref name="labelId"/>, if the row has it.</summary>
    internal LabelFilterChipViewModel? ChipOf(Guid labelId) => _chipsById.GetValueOrDefault(labelId);
}
