using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The width classes of a task column (a Shelf column's card, Today's task list): "columnNarrow" at <see cref="NarrowWidth"/> DIP or
/// less, "columnSlim" at <see cref="SlimWidth"/> or less, set on the marked control as its width changes. The rows' narrow look reads
/// them with plain descendant selectors (TaskRowView). Perf (2026-11-27): it replaces the rows' container queries on "TaskColumn",
/// which made every control of every row register with its column, so taking a column with 50 rows off the page took minutes in a
/// Debug build (and every removed row paid for all the others).
/// </summary>
public static class TaskColumnWidth
{
    /// <summary>At or below this width the label slot goes and the gaps shrink (was the "max-width:520" query).</summary>
    public const double NarrowWidth = 520;

    /// <summary>At or below this width "Add to Today →" moves under the sub-line (was the "max-width:340" query).</summary>
    public const double SlimWidth = 340;

    public const string NarrowClass = "columnNarrow";

    public const string SlimClass = "columnSlim";

    public static readonly AttachedProperty<bool> TracksProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Tracks", typeof(TaskColumnWidth));

    static TaskColumnWidth()
    {
        TracksProperty.Changed.AddClassHandler<Control>(OnTracksChanged);
    }

    public static bool GetTracks(Control control) => control.GetValue(TracksProperty);

    public static void SetTracks(Control control, bool value) => control.SetValue(TracksProperty, value);

    private static void OnTracksChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            control.SizeChanged += OnSizeChanged;
            Apply(control, control.Bounds.Width);
        }
        else
        {
            control.SizeChanged -= OnSizeChanged;
            control.Classes.Remove(NarrowClass);
            control.Classes.Remove(SlimClass);
        }
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Control control && e.WidthChanged)
        {
            Apply(control, e.NewSize.Width);
        }
    }

    // No width yet (not laid out): the wide look, as the container query had before its container was measured.
    private static void Apply(Control control, double width)
    {
        if (width <= 0)
        {
            return;
        }

        control.Classes.Set(NarrowClass, width <= NarrowWidth);
        control.Classes.Set(SlimClass, width <= SlimWidth);
    }
}
