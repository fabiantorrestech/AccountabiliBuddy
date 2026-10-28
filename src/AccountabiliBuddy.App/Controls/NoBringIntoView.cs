using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Stops a control's own scrolling into view (a ListBox scrolls its chosen item into view when the selection is set) from
/// reaching the page's scroll viewer: otherwise a segmented choice halfway down a page scrolls the page to itself the first
/// time the page shows. <c>controls:NoBringIntoView.Suppress="True"</c>.
/// </summary>
public static class NoBringIntoView
{
    public static readonly AttachedProperty<bool> SuppressProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("Suppress");

    static NoBringIntoView()
    {
        SuppressProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
            {
                control.AddHandler(Control.RequestBringIntoViewEvent, OnRequest);
            }
            else
            {
                control.RemoveHandler(Control.RequestBringIntoViewEvent, OnRequest);
            }
        });
    }

    public static bool GetSuppress(Control control) => control.GetValue(SuppressProperty);

    public static void SetSuppress(Control control, bool value) => control.SetValue(SuppressProperty, value);

    private static void OnRequest(object? sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }
}
