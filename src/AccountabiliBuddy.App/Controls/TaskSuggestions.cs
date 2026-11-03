using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Ties a box where a task is typed to its "From your lists" picker: <c>controls:TaskSuggestions.Picker="{Binding ListPicker}"</c>. Tasks
/// matching the text show under the box (<c>TaskSuggestionsView</c>). Enter still adds what was typed; Down moves into the suggestions, Enter
/// then picks one and Esc closes them. A pick empties the box, unless <c>KeepsText</c> (the owner fills it).
/// </summary>
public static class TaskSuggestions
{
    public static readonly AttachedProperty<ListPickerViewModel?> PickerProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, ListPickerViewModel?>("Picker");

    public static readonly AttachedProperty<bool> KeepsTextProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("KeepsText");

    static TaskSuggestions()
    {
        PickerProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            if (e.OldValue is ListPickerViewModel old)
            {
                old.Picked -= PickedHandlerOf(box);
            }

            if (e.NewValue is ListPickerViewModel picker)
            {
                picker.Picked += PickedHandlerOf(box);
                if (!box.GetValue(HookedProperty))
                {
                    box.SetValue(HookedProperty, true);
                    box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
                    box.AddHandler(InputElement.TextInputEvent, (_, _) => { }, RoutingStrategies.Bubble);
                    box.PropertyChanged += OnBoxPropertyChanged;
                    box.LostFocus += OnLostFocus;
                }
            }
        });
    }

    private static readonly AttachedProperty<bool> HookedProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("Hooked");

    private static readonly AttachedProperty<EventHandler?> PickedHandlerProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, EventHandler?>("PickedHandler");

    public static ListPickerViewModel? GetPicker(TextBox box) => box.GetValue(PickerProperty);

    public static void SetPicker(TextBox box, ListPickerViewModel? value) => box.SetValue(PickerProperty, value);

    public static bool GetKeepsText(TextBox box) => box.GetValue(KeepsTextProperty);

    public static void SetKeepsText(TextBox box, bool value) => box.SetValue(KeepsTextProperty, value);

    private static EventHandler PickedHandlerOf(TextBox box)
    {
        EventHandler? handler = box.GetValue(PickedHandlerProperty);
        if (handler is null)
        {
            handler = (_, _) =>
            {
                if (!box.GetValue(KeepsTextProperty))
                {
                    box.Text = string.Empty;
                }
            };
            box.SetValue(PickedHandlerProperty, handler);
        }

        return handler;
    }

    private static void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Only while the keyboard is in the box, so the text changing from anywhere else (a saved value) suggests nothing.
        if (e.Property == TextBox.TextProperty && sender is TextBox { IsFocused: true } box)
        {
            box.GetValue(PickerProperty)?.Typed(box.Text ?? string.Empty);
        }
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.GetValue(PickerProperty)?.DismissSuggestions();
        }
    }

    // Tunnel: ahead of the box's own Enter binding, and only taken when a suggestion is in play.
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.GetValue(PickerProperty) is not ListPickerViewModel picker)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                e.Handled = picker.MoveSelection(1);
                break;
            case Key.Up:
                e.Handled = picker.MoveSelection(-1);
                break;
            case Key.Enter:
                e.Handled = picker.PickSelected();
                break;
            case Key.Escape when picker.HasSuggestions:
                picker.DismissSuggestions();
                e.Handled = true;
                break;
        }
    }
}
