using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>Today's main card while a session runs. Code-behind: the "Note for this session" flyout of the ⋯ menu.</summary>
public partial class FocusView : UserControl
{
    public FocusView()
    {
        InitializeComponent();
    }

    private Flyout NoteFlyout => (Flyout)Resources["NoteFlyout"]!;

    // The menu closes first, then the note line opens on the ⋯ button that was used.
    private void OnNoteClick(object? sender, RoutedEventArgs e)
    {
        Button anchor = DataContext is FocusViewModel { IsPlanning: true } ? MorePlanningButton : MoreRunningButton;
        Dispatcher.UIThread.Post(() => NoteFlyout.ShowAt(anchor), DispatcherPriority.Background);
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
        if (e.Key == Key.Enter && DataContext is FocusViewModel { Capture: { } capture })
        {
            capture.SaveCommand.Execute(null);
            NoteFlyout.Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            NoteFlyout.Hide();
            e.Handled = true;
        }
    }
}
