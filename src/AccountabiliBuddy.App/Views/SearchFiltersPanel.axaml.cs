using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Search filters panel's code-behind (Tasks redesign 7.09-7.11, 7.41): where it sits in its layer (its right edge under the search box's
/// right edge, its top where the host says, never taller than the room it is given; the sections scroll inside) and Esc, which closes it.
/// </summary>
public partial class SearchFiltersPanel : UserControl
{
    private const double MinPanelHeight = 160;

    public SearchFiltersPanel()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Places the panel in <paramref name="layer"/> (a full-size layer it is aligned top-right in): its right edge under
    /// <paramref name="anchor"/>'s right edge, its top <paramref name="top"/> from the layer's top, at most <paramref name="maxHeight"/> tall.
    /// </summary>
    public void PlaceIn(Visual layer, Control anchor, double top, double maxHeight)
    {
        if (anchor.TranslatePoint(new Point(anchor.Bounds.Width, 0), layer) is not Point right)
        {
            return;
        }

        Margin = new Thickness(0, Math.Max(0, top), Math.Max(0, layer.Bounds.Width - right.X), 0);
        MaxHeight = Math.Max(MinPanelHeight, maxHeight);
    }

    // Esc closes the panel (as Done does); a date picker's open calendar takes its own Esc first.
    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && DataContext is SearchFiltersViewModel panel)
        {
            panel.DoneCommand.Execute(null);
            e.Handled = true;
        }
    }
}
