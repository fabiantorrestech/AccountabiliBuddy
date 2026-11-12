using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The dimmed full screen of a Can't miss reminder (and of every reminder with "How reminders show" on Always full screen), spec 03
/// §D: one window covers one whole screen, taskbar included; the primary screen's shows the card (<see cref="ShowCard"/>), the others
/// only dim. Shown without taking the focus from the user's work: the first click on it activates it.
/// </summary>
public partial class CantMissWindow : Window
{
    public CantMissWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows the centred card on this window (the primary screen's).</summary>
    public void ShowCard()
    {
        Card.IsVisible = true;
    }

    /// <summary>Covers <paramref name="screen"/> (its whole bounds, in its own scaling once open on it).</summary>
    public void Cover(Screen screen)
    {
        PixelRect bounds = screen.Bounds;
        Position = bounds.Position;
        Width = bounds.Width / screen.Scaling;
        Height = bounds.Height / screen.Scaling;
        Opened += (_, _) =>
        {
            double scaling = DesktopScaling;
            Width = bounds.Width / scaling;
            Height = bounds.Height / scaling;
            Position = bounds.Position;
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Esc closes the Snooze menu; it never dismisses a reminder.
        if (e.Key == Key.Escape && DataContext is ReminderStackViewModel { IsSnoozeOpen: true } stack)
        {
            stack.CloseSnooze();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
