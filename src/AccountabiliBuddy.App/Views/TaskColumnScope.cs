using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Marks the Tasks page's Shelf columns (round 2 R3): rows inside draw the column look (the label's dot before the check box, its name
/// first on the sub-line, no label slot at the right, ⌄ and ⋯ only on hover or focus) and take ← to send the task to Today › Queued.
/// Inherited, so every row under the marked column reads it; the same row view elsewhere (Today) is unchanged.
/// </summary>
public static class TaskColumnScope
{
    public static readonly AttachedProperty<bool> IsColumnProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsColumn", typeof(TaskColumnScope), inherits: true);

    public static bool GetIsColumn(Control control) => control.GetValue(IsColumnProperty);

    public static void SetIsColumn(Control control, bool value) => control.SetValue(IsColumnProperty, value);
}
