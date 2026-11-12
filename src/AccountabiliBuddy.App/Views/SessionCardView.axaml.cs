using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Today's session card (round 2). Code-behind: the two ⋯ menus (built when they open, in the shared menu look, <see cref="TaskMenus"/>),
/// the forms they open on their button (Pomodoro, Profile / length / break, the note line), and the note line's keys.
/// </summary>
public partial class SessionCardView : UserControl
{
    private Button? _noteAnchor;

    public SessionCardView()
    {
        InitializeComponent();
    }

    private Flyout NoteFlyout => (Flyout)Resources["NoteFlyout"]!;

    private Flyout PomodoroFlyout => (Flyout)Resources["PomodoroFlyout"]!;

    private Flyout StartOptionsFlyout => (Flyout)Resources["StartOptionsFlyout"]!;

    // The running or planning ⋯: Interrupted, Side task, Add 10 minutes, the note, the toggles, End session (the same end flow).
    private void OnSessionMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not SessionViewModel { Focus: { } focus })
        {
            return;
        }

        TaskMenus.ShowEntries(button, focus.CardMenu(), PlacementMode.BottomEdgeAlignedLeft, entry =>
        {
            if (entry.ViewAction == FocusViewModel.NoteAction)
            {
                ShowLater(NoteFlyout, button);
            }
        });
        e.Handled = true;
    }

    // The idle ⋯: other ways to start, Plan day(s), Start shutdown, Back to work.
    private void OnStartMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not SessionViewModel session)
        {
            return;
        }

        TaskMenus.ShowEntries(button, session.StartMenu(), PlacementMode.BottomEdgeAlignedLeft, entry =>
        {
            if (entry.ViewAction == SessionViewModel.PomodoroAction)
            {
                ShowLater(PomodoroFlyout, button);
            }
            else if (entry.ViewAction == SessionViewModel.StartOptionsAction)
            {
                ShowLater(StartOptionsFlyout, button);
            }
        });
        e.Handled = true;
    }

    // The note line under the buttons.
    private void OnNoteClick(object? sender, RoutedEventArgs e)
    {
        ShowLater(NoteFlyout, NoteButton);
    }

    // The menu closes first, then the form opens on the same button.
    private void ShowLater(Flyout flyout, Button anchor)
    {
        _noteAnchor = anchor;
        Dispatcher.UIThread.Post(() => flyout.ShowAt(anchor), DispatcherPriority.Background);
    }

    // The options panel's fold is the flyout itself: it starts open.
    private void OnStartOptionsOpening(object? sender, EventArgs e)
    {
        if (DataContext is SessionViewModel viewModel)
        {
            viewModel.QuickStart.ShowsOptions = true;
        }
    }

    // The line takes the keyboard as it opens.
    private void OnNoteOpened(object? sender, EventArgs e)
    {
        if (NoteFlyout.Content is Panel panel && panel.Children.OfType<TextBox>().FirstOrDefault() is { } box)
        {
            Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Background);
        }
    }

    // Enter keeps the note (the capture box's own Save) and closes the line; Escape just closes it.
    private void OnNoteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SessionViewModel { Focus.Capture: { } capture })
        {
            capture.SaveCommand.Execute(null);
            NoteFlyout.Hide();
            _noteAnchor?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            NoteFlyout.Hide();
            e.Handled = true;
        }
    }
}
