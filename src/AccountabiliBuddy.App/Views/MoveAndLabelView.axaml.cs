using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Move and label's view (Tasks redesign 8c; its data context is a <see cref="MoveAndLabelViewModel"/>, null while closed). Code-behind only
/// for what the view model can't see: the modifier keys of a click on a row (Ctrl+click, Shift+click), Ctrl+A with the list focused, Esc
/// (the Show list closes first, then typed text in the New tag box; the label search clears its own text first; otherwise the dialog
/// closes), Enter in the New tag box, Ctrl+F to the label search, a click outside the Show list closing it, the keyboard going into the
/// dialog as it opens and back as it closes, the appear animation, and the label picker taking the room the window leaves (never under
/// 240, the side scrolling below that).
/// </summary>
public partial class MoveAndLabelView : UserControl
{
    /// <summary>A current value with a proposed change after it shows at 60% (checklist 8c.18, 8c.19).</summary>
    public static readonly IValueConverter StruckOpacity = new FuncValueConverter<bool, double>(struck => struck ? 0.6 : 1);

    private KeyModifiers _pressModifiers;
    private bool _pointerClick;
    private IInputElement? _focusBefore;

    public MoveAndLabelView()
    {
        InitializeComponent();
        TaskList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        TaskList.AddHandler(Button.ClickEvent, OnListClick, RoutingStrategies.Bubble);
        Frame.AddHandler(PointerPressedEvent, OnFramePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        TagList.AddHandler(KeyDownEvent, OnNewTagKeyDown, RoutingStrategies.Tunnel);
        ChoicesScroll.PropertyChanged += (_, change) =>
        {
            if (change.Property == ScrollViewer.ViewportProperty)
            {
                FitChoices();
            }
        };
    }

    private MoveAndLabelViewModel? Dialog => DataContext as MoveAndLabelViewModel;

    // Opened: the keyboard goes into the task list and the frame fades in; closed: the keyboard goes back to where it was.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MoveAndLabelViewModel)
        {
            _focusBefore ??= TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Frame.Classes.Add("entering");
            Dispatcher.UIThread.Post(() =>
            {
                Frame.Classes.Remove("entering");
                if (IsEffectivelyVisible)
                {
                    TaskList.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Loaded);
            return;
        }

        IInputElement? before = _focusBefore;
        _focusBefore = null;
        if (before is InputElement element && element.IsEffectivelyVisible && element.Focusable && TopLevel.GetTopLevel(element) is not null
            && !this.IsVisualAncestorOf(element))
        {
            Dispatcher.UIThread.Post(() => element.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
        }
    }

    // The label picker takes what the side leaves (the grid is at least the viewport's height); below 240 the side scrolls (8c.41, 8c.49).
    private void FitChoices()
    {
        double height = ChoicesScroll.Viewport.Height - ChoicesBody.Margin.Top - ChoicesBody.Margin.Bottom;
        ChoicesBody.MinHeight = Math.Max(0, height);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    // A click outside the Show list (and its button) closes it; the click goes on to what it was on.
    private void OnFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Dialog is { IsShowMenuOpen: true } dialog && e.Source is Visual source
            && !ShowMenu.IsVisualAncestorOf(source) && !ReferenceEquals(ShowMenu, source)
            && !ShowButton.IsVisualAncestorOf(source) && !ReferenceEquals(ShowButton, source))
        {
            dialog.IsShowMenuOpen = false;
        }
    }

    // ---- The rows: plain click, Ctrl+click, Shift+click (8c.23) ----

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressModifiers = e.KeyModifiers;
        _pointerClick = true;
    }

    private void OnListClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { DataContext: MoveAndLabelRowViewModel row } button || !button.Classes.Contains("mlRowButton") || Dialog is not { } dialog)
        {
            _pointerClick = false;
            return;
        }

        KeyModifiers modifiers = _pointerClick ? _pressModifiers : KeyModifiers.None;
        _pointerClick = false;
        TaskRowClick click = modifiers.HasFlag(KeyModifiers.Shift) ? TaskRowClick.AddRange
            : modifiers.HasFlag(KeyModifiers.Control) ? TaskRowClick.Toggle
            : TaskRowClick.Open;
        dialog.Click(row, click);
        e.Handled = true;
    }

    // ---- Keys ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Dialog is not { } dialog)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            // 8c.67: the label search's own Esc already cleared its text (and kept the key); here the Show list, then New tag text, then close.
            if (dialog.IsShowMenuOpen)
            {
                dialog.IsShowMenuOpen = false;
                ShowButton.Focus(NavigationMethod.Directional);
            }
            else if (IsNewTagBox(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()) && !string.IsNullOrEmpty(dialog.NewTagText))
            {
                dialog.NewTagText = string.Empty;
            }
            else
            {
                dialog.DoneCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control && IsKeyboardIn(TaskList))
        {
            dialog.EditAllShownCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            // Ctrl+F in the dialog finds a label (the page's own search is behind the dim).
            Picker.FocusSearch();
            e.Handled = true;
        }
    }

    private void OnNewTagKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && IsNewTagBox(e.Source) && Dialog is { } dialog)
        {
            dialog.AddNewTag();
            e.Handled = true;
        }
    }

    private static bool IsNewTagBox(object? element) => element is TextBox box && box.Classes.Contains("mlNewTag");

    private bool IsKeyboardIn(Visual area)
    {
        return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused
            && (ReferenceEquals(focused, area) || area.IsVisualAncestorOf(focused));
    }
}
