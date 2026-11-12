using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The toast stack's own topmost window (spec 03 §D): bottom right of the primary screen's work area, shown without taking the focus
/// from the user's work. It grows upwards (the Snooze menu, the list view) and lifts above any of the app's toasts
/// (<see cref="NudgeToast"/>) that would overlap it; <see cref="Place"/> runs on every size change and once a second from the ringer.
/// </summary>
public partial class ReminderStackWindow : Window
{
    // DIP; with an auto-hiding taskbar the work area reaches the screen's edge, but the taskbar still pops up over it.
    private const double AutoHideTaskbarHeight = 64;

    private const double EstimatedHeight = 230;

    public ReminderStackWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Place();
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Puts the window's bottom right on the primary screen's work area, lifted above an overlapping toast of the app.</summary>
    public void Place()
    {
        Screen? screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        // Before its first layout (placed before it shows, so it doesn't flash elsewhere) its height is a card's, about.
        double widthDip = Bounds.Width > 0 ? Bounds.Width : Width;
        double heightDip = Bounds.Height > 0 ? Bounds.Height : EstimatedHeight;
        double scaling = screen.Scaling;
        PixelRect work = screen.WorkingArea;
        int width = (int)Math.Ceiling(widthDip * scaling);
        int height = (int)Math.Ceiling(heightDip * scaling);
        int bottom = work.Bottom == screen.Bounds.Bottom ? work.Bottom - (int)Math.Ceiling(AutoHideTaskbarHeight * scaling) : work.Bottom;
        int left = work.Right - width;
        bottom = LiftAboveToasts(left, width, bottom, height);

        var position = new PixelPoint(left, Math.Max(work.Y, bottom - height));
        if (Position != position)
        {
            Position = position;
        }
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

    // The bottom edge moves up past every toast of the app that sits in the stack's column (each one found moves it again).
    private static int LiftAboveToasts(int left, int width, int bottom, int height)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return bottom;
        }

        List<PixelRect> toasts = desktop.Windows.OfType<NudgeToast>().Where(toast => toast.IsVisible).Select(BoxOf).ToList();
        bool moved = true;
        for (int round = 0; moved && round <= toasts.Count; round++)
        {
            moved = false;
            foreach (PixelRect toast in toasts)
            {
                bool sameColumn = toast.X < left + width && left < toast.Right;
                bool overlaps = toast.Y < bottom && bottom - height < toast.Bottom;
                if (sameColumn && overlaps && toast.Y < bottom)
                {
                    bottom = toast.Y;
                    moved = true;
                }
            }
        }

        return bottom;
    }

    private static PixelRect BoxOf(Window window)
    {
        double scaling = window.DesktopScaling;
        return new PixelRect(window.Position, new PixelSize((int)Math.Ceiling(window.Bounds.Width * scaling), (int)Math.Ceiling(window.Bounds.Height * scaling)));
    }
}
