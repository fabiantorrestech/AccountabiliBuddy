using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// "Snooze until…" (spec 03 §C). Code-behind: a summary part puts the keyboard in its field (the month opens its list), the fields
/// commit when they lose the keyboard, Enter snoozes (in the "type it" box it reads the words instead), and the year flashes when it
/// rolls over. Show it with <see cref="ShowAsync"/>; M6's reminder cards reuse it.
/// </summary>
public partial class SnoozePickerDialog : TaskDialogWindow
{
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(1400);

    public SnoozePickerDialog()
    {
        InitializeComponent();
    }

    public SnoozePickerDialog(SnoozePickerViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, moment) => Close(moment);
        viewModel.YearRolled += (_, _) => FlashYear();
    }

    /// <summary>Shows the picker over <paramref name="opener"/>'s window; the moment picked, or null when it was cancelled.</summary>
    public static async Task<DateTimeOffset?> ShowAsync(Visual? opener, SnoozePickerViewModel viewModel)
    {
        object? result = await new SnoozePickerDialog(viewModel).ShowOverAsync(opener);
        return result is DateTimeOffset moment ? moment : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Enter && DataContext is SnoozePickerViewModel picker)
        {
            // The field with the keyboard commits first (it has not lost it).
            picker.CommitTime();
            if (picker.SnoozeCommand.CanExecute(null))
            {
                picker.SnoozeCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnMonthPartClick(object? sender, RoutedEventArgs e)
    {
        MonthBox.Focus(NavigationMethod.Tab);
        MonthBox.IsDropDownOpen = true;
    }

    private void OnDayPartClick(object? sender, RoutedEventArgs e) => FocusField(DayBox);

    private void OnYearPartClick(object? sender, RoutedEventArgs e) => FocusField(YearBox);

    private void OnTimePartClick(object? sender, RoutedEventArgs e) => FocusField(TimeBox);

    private static void FocusField(TextBox box)
    {
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
    }

    private void OnDayLostFocus(object? sender, RoutedEventArgs e) => (DataContext as SnoozePickerViewModel)?.CommitDay();

    private void OnTimeLostFocus(object? sender, RoutedEventArgs e) => (DataContext as SnoozePickerViewModel)?.CommitTime();

    private void OnYearLostFocus(object? sender, RoutedEventArgs e) => (DataContext as SnoozePickerViewModel)?.CommitYear();

    private void OnNaturalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SnoozePickerViewModel picker)
        {
            picker.ApplyNaturalCommand.Execute(null);
            e.Handled = true;
        }
    }

    // The year field glows and fades (no glow when Motion is off).
    private void FlashYear()
    {
        TimeSpan time = Motion.Current?.Scale(FlashTime) ?? FlashTime;
        if (time <= TimeSpan.Zero)
        {
            return;
        }

        YearFlash.Transitions = null;
        YearFlash.Opacity = 0.3;
        Dispatcher.UIThread.Post(() =>
        {
            YearFlash.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = time }];
            YearFlash.Opacity = 0;
        }, DispatcherPriority.Render);
    }
}
