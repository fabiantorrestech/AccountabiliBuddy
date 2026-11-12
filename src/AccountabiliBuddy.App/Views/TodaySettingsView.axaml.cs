using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Settings › Today (<see cref="TodaySettingsViewModel"/>). Code-behind: dragging a row's ≡ (it follows the pointer; the drop moves it
/// once), Alt+↑/↓ on a focused ≡ (the moved row keeps the keyboard), and "scrolled to that part" when Today's Options opens a group.
/// </summary>
public partial class TodaySettingsView : UserControl
{
    private const double DragStart = 4;

    private Border? _dragRow;
    private string? _dragKey;
    private Point _pressedAt;
    private bool _dragging;

    public TodaySettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TodaySettingsViewModel viewModel && !ReferenceEquals(viewModel, _subscribed))
            {
                _subscribed = viewModel;
                viewModel.RevealRequested += (_, area) => Dispatcher.UIThread.Post(() => Reveal(area), DispatcherPriority.Background);
            }
        };
    }

    private TodaySettingsViewModel? _subscribed;

    /// <summary>Brings the group with that search area name to the top (Today's Options › Arrange cards… / Sidebar items and order…).</summary>
    public void Reveal(string area)
    {
        Control? group = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(this).OfType<Control>()
            .FirstOrDefault(control => Services.Search.SearchArea.GetName(control) == area);
        group?.BringIntoView();
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: TodayPartRowViewModel row } grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragKey = row.Key;
        _dragRow = grip.FindAncestorOfType<Border>();
        _pressedAt = e.GetPosition(PartsList);
        _dragging = false;
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragKey is null)
        {
            return;
        }

        Point now = e.GetPosition(PartsList);
        if (!_dragging && Math.Abs(now.Y - _pressedAt.Y) < DragStart)
        {
            return;
        }

        _dragging = true;
        _dragRow?.Classes.Set("dragging", true);
    }

    private async void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        string? key = _dragKey;
        bool dragged = _dragging;
        Point at = e.GetPosition(PartsList);
        EndDrag();
        e.Pointer.Capture(null);
        if (key is null || !dragged || DataContext is not TodaySettingsViewModel viewModel)
        {
            return;
        }

        await viewModel.MoveAsync(key, IndexAt(at.Y, key));
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndDrag();
    }

    private void EndDrag()
    {
        _dragRow?.Classes.Set("dragging", false);
        _dragRow = null;
        _dragKey = null;
        _dragging = false;
    }

    // Where a drop at y lands among the other rows: before the first row whose middle is below it.
    private int IndexAt(double y, string key)
    {
        List<Control> rows = PartsList.GetRealizedContainers().ToList();
        int index = 0;
        foreach (Control row in rows)
        {
            if (row.DataContext is TodayPartRowViewModel { Key: var rowKey } && rowKey == key)
            {
                continue;
            }

            Point? top = row.TranslatePoint(new Point(0, 0), PartsList);
            if (top is { } corner && y < corner.Y + row.Bounds.Height / 2)
            {
                return index;
            }

            index++;
        }

        return index;
    }

    // Alt+↑/↓ on a row's ≡: one place up or down; the ≡ of the moved row takes the keyboard back.
    private async void OnGripKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down)
            || sender is not Border { DataContext: TodayPartRowViewModel row } || DataContext is not TodaySettingsViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        string key = row.Key;
        await viewModel.MoveByAsync(key, e.Key == Key.Up ? -1 : 1);
        Dispatcher.UIThread.Post(() =>
        {
            Border? grip = PartsList.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("grip") && border.DataContext is TodayPartRowViewModel { Key: var movedKey } && movedKey == key);
            grip?.Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Background);
    }
}
