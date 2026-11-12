using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Marks the Today page's task list (round 2): the rows it holds are the same view models as the Tasks page's, but only Today draws the
/// running row's tint, its Now badge and its steps open (brief §4.2). Inherited, so every row under the marked container reads it.
/// </summary>
public static class TodayPageScope
{
    public static readonly AttachedProperty<bool> IsTodayProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsToday", typeof(TodayPageScope), inherits: true);

    public static bool GetIsToday(Control control) => control.GetValue(IsTodayProperty);

    public static void SetIsToday(Control control, bool value) => control.SetValue(IsTodayProperty, value);
}
