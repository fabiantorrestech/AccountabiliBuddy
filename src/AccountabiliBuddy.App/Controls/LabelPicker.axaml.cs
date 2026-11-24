using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The shared label picker's view (its data context is a <see cref="LabelPickerViewModel"/>). Code-behind only for the keys: in the search
/// field Enter picks the first match, Esc clears typed text (and is kept from the dialog; with no text it goes on and closes it), ↓ goes to
/// the labels; in the labels ↑/↓ move and ← goes to the rail; in the rail ↑/↓ move and show that category, → goes to the labels. Enter or
/// Space on a row clicks it (a second click on the picked label takes the pick back).
/// </summary>
public partial class LabelPicker : UserControl
{
    /// <summary>What sits left of "No label" on the first row (Move and label: "Label · Now: Work-ePT"); null for none.</summary>
    public static readonly StyledProperty<object?> HeaderProperty = AvaloniaProperty.Register<LabelPicker, object?>(nameof(Header));

    /// <summary>Whether the "No label" button shows (on by default).</summary>
    public static readonly StyledProperty<bool> ShowsNoLabelProperty = AvaloniaProperty.Register<LabelPicker, bool>(nameof(ShowsNoLabel), true);

    public LabelPicker()
    {
        InitializeComponent();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        RailList.AddHandler(KeyDownEvent, OnRailKeyDown, RoutingStrategies.Bubble);
        LabelList.AddHandler(KeyDownEvent, OnLabelsKeyDown, RoutingStrategies.Bubble);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool ShowsNoLabel
    {
        get => GetValue(ShowsNoLabelProperty);
        set => SetValue(ShowsNoLabelProperty, value);
    }

    private LabelPickerViewModel? Model => DataContext as LabelPickerViewModel;

    /// <summary>Puts the keyboard in the search field (a caller opening the picker to type).</summary>
    public void FocusSearch() => SearchBox.Focus(NavigationMethod.Tab);

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                model.PickFirstMatch();
                e.Handled = true;
                break;
            case Key.Escape when !string.IsNullOrEmpty(model.SearchText):
                model.SearchText = string.Empty;
                e.Handled = true;
                break;
            case Key.Down:
                e.Handled = FocusAt(LabelButtons(), 0);
                break;
        }
    }

    private void OnLabelsKeyDown(object? sender, KeyEventArgs e)
    {
        List<Button> labels = LabelButtons();
        int at = labels.FindIndex(button => button.IsFocused);
        if (at < 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                e.Handled = FocusAt(labels, Math.Min(at + 1, labels.Count - 1));
                break;
            case Key.Up when at == 0:
                SearchBox.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Up:
                e.Handled = FocusAt(labels, at - 1);
                break;
            case Key.Left:
                List<Button> rail = RailButtons();
                int shown = Model?.ShownCategory is { } category && Model.Rail.ToList().IndexOf(category) is var index and >= 0 ? index : 0;
                e.Handled = FocusAt(rail, shown);
                break;
        }
    }

    // Moving through the rail shows each category as it is reached (the labels follow the keyboard, like tabs).
    private void OnRailKeyDown(object? sender, KeyEventArgs e)
    {
        List<Button> rail = RailButtons();
        int at = rail.FindIndex(button => button.IsFocused);
        if (at < 0 || Model is not { } model)
        {
            return;
        }

        int next = e.Key switch
        {
            Key.Down => Math.Min(at + 1, rail.Count - 1),
            Key.Up => Math.Max(at - 1, 0),
            _ => -1,
        };
        if (next >= 0)
        {
            if (next < model.Rail.Count)
            {
                model.SelectCategory(model.Rail[next]);
            }

            e.Handled = FocusAt(rail, next);
        }
        else if (e.Key == Key.Right)
        {
            e.Handled = FocusAt(LabelButtons(), 0);
        }
    }

    private List<Button> LabelButtons() => LabelList.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("lpLabel")).ToList();

    private List<Button> RailButtons() => RailList.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("lpCategory")).ToList();

    private static bool FocusAt(List<Button> buttons, int index)
    {
        if (index < 0 || index >= buttons.Count)
        {
            return false;
        }

        buttons[index].Focus(NavigationMethod.Directional);
        buttons[index].BringIntoView();
        return true;
    }
}
