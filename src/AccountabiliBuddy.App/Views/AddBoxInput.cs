using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The keyboard and focus of an add box's text box (<see cref="AddBoxView"/>, <see cref="JotBoxView"/>), tied to its
/// <see cref="AddBoxViewModel"/>: ↑/↓, Enter or Tab and Esc in the "@" / "#" list (ahead of the box's own keys), Enter adds, Esc
/// empties or closes, Backspace at the start takes the last chip, Alt+←/→ moves the split, and the text and caret are reported as
/// they change. A menu opened from the box keeps it in use until it closes, then the keyboard comes back to the box.
/// </summary>
internal sealed class AddBoxInput
{
    private readonly TextBox _box;
    private AddBoxViewModel? _model;
    private bool _menuOpen;
    private bool _typedQueued;

    public AddBoxInput(TextBox box)
    {
        _box = box;
        _box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _box.PropertyChanged += OnBoxPropertyChanged;
        _box.GotFocus += OnGotFocus;
        _box.LostFocus += OnLostFocus;
    }

    public void Attach(AddBoxViewModel? model)
    {
        if (_model is not null)
        {
            _model.FocusRequested -= OnFocusRequested;
            _model.LeaveRequested -= OnLeaveRequested;
            _model.CaretRequested -= OnCaretRequested;
        }

        _model = model;
        if (_model is not null)
        {
            _model.FocusRequested += OnFocusRequested;
            _model.LeaveRequested += OnLeaveRequested;
            _model.CaretRequested += OnCaretRequested;
        }
    }

    /// <summary>
    /// Opens a menu by <paramref name="anchor"/>; the box stays in use while it shows. Then the keyboard goes back to the box, unless
    /// <paramref name="afterClose"/> takes over (it returns true when it moved the keyboard somewhere else).
    /// </summary>
    public void ShowMenu(Control anchor, IReadOnlyList<TaskMenuEntry> entries, PlacementMode placement, Action<TaskMenuEntry>? viewAction = null, Func<bool>? afterClose = null)
    {
        _menuOpen = true;
        TaskMenus.ShowEntries(anchor, entries, placement, viewAction, () =>
        {
            _menuOpen = false;
            if (afterClose?.Invoke() != true)
            {
                FocusBox();
            }
        });
    }

    /// <summary>Keeps the box in use while something of its own (a picker) has the keyboard.</summary>
    public void HoldOpen(bool held)
    {
        _menuOpen = held;
    }

    public void FocusBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_box.IsEffectivelyVisible)
            {
                _box.Focus();
                _box.CaretIndex = _box.Text?.Length ?? 0;
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnFocusRequested(object? sender, EventArgs e) => FocusBox();

    private void OnLeaveRequested(object? sender, EventArgs e) => Leave();

    /// <summary>
    /// The box lets go of the keyboard (Esc, or a click anywhere off it): it folds back to its pill through the usual lost-focus path,
    /// and what was typed stays as a draft. Nothing happens while one of its own menus is open or the keyboard is elsewhere.
    /// </summary>
    public void Leave()
    {
        if (_box.IsFocused && !_menuOpen)
        {
            TopLevel.GetTopLevel(_box)?.FocusManager?.Focus(null);
        }
    }

    private void OnCaretRequested(object? sender, int caret)
    {
        Dispatcher.UIThread.Post(() => _box.CaretIndex = Math.Clamp(caret, 0, _box.Text?.Length ?? 0), DispatcherPriority.Input);
    }

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        _model?.SetFocused(true);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_menuOpen || _model is null)
        {
            return;
        }

        // After the press that moved the keyboard is done: a pick in the "@" / "#" list or a send button acts first, and a box that
        // takes the keyboard back at once (after an add) is not closed or shrunk in between.
        Dispatcher.UIThread.Post(() =>
        {
            if (_box.IsFocused || _menuOpen || _model is not AddBoxViewModel model)
            {
                return;
            }

            model.SetFocused(false);
            model.LeftEmpty();
        }, DispatcherPriority.Background);
    }

    // The text and the caret move separately (the caret after the text when typing): the model reads both once they settled.
    private void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if ((e.Property != TextBox.TextProperty && e.Property != TextBox.CaretIndexProperty) || !_box.IsFocused || _typedQueued)
        {
            return;
        }

        _typedQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _typedQueued = false;
            if (_box.IsFocused)
            {
                _model?.Typed(_box.CaretIndex);
            }
        }, DispatcherPriority.Background);
    }

    // Tunnel: ahead of the text box's own keys.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not AddBoxViewModel model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down when e.KeyModifiers == KeyModifiers.None:
                e.Handled = model.MoveSuggestion(1);
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.None:
                e.Handled = model.MoveSuggestion(-1);
                break;
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                e.Handled = model.PickSelectedSuggestion();
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                if (!model.PickSelectedSuggestion())
                {
                    model.SubmitCommand.Execute(null);
                }

                e.Handled = true;
                break;
            case Key.Escape:
                model.Escape();
                e.Handled = true;
                break;
            case Key.Back when _box.CaretIndex == 0 && _box.SelectionStart == _box.SelectionEnd:
                e.Handled = model.RemoveLastChip();
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.Alt:
                e.Handled = model.MoveSplit(-1);
                break;
            case Key.Right when e.KeyModifiers == KeyModifiers.Alt:
                e.Handled = model.MoveSplit(1);
                break;
        }
    }
}
