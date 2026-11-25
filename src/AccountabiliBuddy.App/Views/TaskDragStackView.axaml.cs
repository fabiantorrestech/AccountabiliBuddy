using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>The dragged stack's card (Tasks redesign 8b): drawn in the window's overlay layer by <see cref="TaskStackDrag"/>; no behaviour of its own.</summary>
public partial class TaskDragStackView : UserControl
{
    /// <summary>The room around the top card (its margin in the view): the view is placed this much up and left of where the card goes.</summary>
    public const double StackRoom = 16;

    public TaskDragStackView()
    {
        InitializeComponent();
    }
}
