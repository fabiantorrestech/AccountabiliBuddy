using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The add box of the Tasks page and of a Shelf bin's "+" (<see cref="AddBoxViewModel"/>). Code-behind: the keyboard and focus
/// (<see cref="AddBoxInput"/>), a click anywhere on the pill takes the keyboard, and the Send to and marker menus (they keep the box in
/// use while open). "From your lists…" opens the lists' picker by the ▾.
/// </summary>
public partial class AddBoxView : UserControl
{
    private readonly AddBoxInput _input;
    private Flyout? _fromLists;

    public AddBoxView()
    {
        InitializeComponent();
        _input = new AddBoxInput(Box);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _input.Attach(DataContext as AddBoxViewModel);
    }

    // PC fixes (owner, 0.1.132): a press anywhere off the box folds it back to its pill, also on something that takes no keyboard
    // focus (the page's background, a header). Presses in its own menus and lists are other top levels and never arrive here.
    private TopLevel? _topLevel;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(PointerPressedEvent, OnAnyPointerPressed);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Box.IsFocused && !IsInside(e.Source as Avalonia.Visual))
        {
            _input.Leave();
        }
    }

    private bool IsInside(Avalonia.Visual? source)
    {
        return source is not null && (ReferenceEquals(source, this) || Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(this, source));
    }

    private void OnPillPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!Box.IsFocused)
        {
            _input.FocusBox();
        }
    }

    private void OnDestinationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddBoxViewModel box)
        {
            return;
        }

        bool openLists = false;
        _input.ShowMenu(
            DestinationButton, box.DestinationMenu(), PlacementMode.BottomEdgeAlignedRight,
            entry => openLists = entry.ViewAction == "fromLists",
            () => openLists && ShowFromLists(box));
    }

    private void OnMarkerLabelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddBoxViewModel box)
        {
            _input.ShowMenu(MarkerLabelButton, box.MarkerLabelMenu(), PlacementMode.BottomEdgeAlignedLeft);
        }
    }

    private void OnMarkerTagClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddBoxViewModel box)
        {
            _input.ShowMenu(MarkerTagButton, box.MarkerTagMenu(), PlacementMode.BottomEdgeAlignedLeft);
        }
    }

    // "From your lists…": the picker by the ▾; the box stays in use until it closes, then the keyboard comes back.
    private bool ShowFromLists(AddBoxViewModel box)
    {
        if (box.FromYourLists is not ListPickerViewModel picker)
        {
            return false;
        }

        if (_fromLists is null)
        {
            _fromLists = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, Content = new ListPickerView { DataContext = picker } };
            _fromLists.Closed += (_, _) =>
            {
                _input.HoldOpen(false);
                _input.FocusBox();
            };
            picker.Closed += (_, _) => _fromLists?.Hide();
        }

        _input.HoldOpen(true);
        picker.Open();
        _fromLists.ShowAt(DestinationButton);
        return true;
    }
}
