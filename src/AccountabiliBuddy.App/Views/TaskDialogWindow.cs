using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Tasks page's dialogs (spec 03 §C: Changed on both devices, Snooze until…, Rename one by one, Delete N, Create/Edit view, the
/// weekly tidy-up): modal over the window that opened them (so the keyboard stays inside them), Esc closes, and the keyboard goes back to
/// the row or control that had it once they close. The theme's surface, line and popover radius come from resources, so they follow
/// the theme live. Show one with <see cref="ShowOverAsync"/>.
/// </summary>
public class TaskDialogWindow : Window
{
    public TaskDialogWindow()
    {
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Ab.Surface"));
        Opened += (_, _) => Dispatcher.UIThread.Post(FocusStart, DispatcherPriority.Loaded);
        // Centred over a small owner near a screen edge (a reminder toast at the bottom right), it would hang off the screen.
        Opened += (_, _) => Dispatcher.UIThread.Post(KeepOnScreen, DispatcherPriority.Loaded);
    }

    // Look like any other window (a derived class has no theme of its own).
    protected override Type StyleKeyOverride => typeof(Window);

    /// <summary>
    /// Shows the dialog modally over the window holding <paramref name="opener"/> (else the main window), and gives the keyboard back to
    /// whatever had it before (the row or button that opened it) once it closes. Returns what the dialog closed with.
    /// </summary>
    public async Task<object?> ShowOverAsync(Visual? opener)
    {
        Window? owner = opener is null ? null : TopLevel.GetTopLevel(opener) as Window;
        owner ??= (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        IInputElement? before = owner?.FocusManager?.GetFocusedElement();
        object? result;
        if (owner is { IsVisible: true })
        {
            result = await ShowDialog<object?>(owner);
        }
        else
        {
            var closed = new TaskCompletionSource<object?>();
            Closed += (_, _) => closed.TrySetResult(null);
            Show();
            result = await closed.Task;
        }

        GiveFocusBack(before, opener);
        return result;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    // The control marked "dialogStart" takes the keyboard as the dialog opens (the newer version, the name box, the first decision).
    private void FocusStart()
    {
        Control? start = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Classes.Contains("dialogStart") && control.IsEffectivelyVisible);
        start?.Focus();
        if (start is TextBox box)
        {
            box.SelectAll();
        }
    }

    // Moves the dialog inside the work area of the screen it opened on, when part of it is outside.
    private void KeepOnScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }

        PixelRect work = screen.WorkingArea;
        int width = (int)Math.Ceiling(Bounds.Width * DesktopScaling);
        int height = (int)Math.Ceiling(Bounds.Height * DesktopScaling);
        int x = Math.Max(work.X, Math.Min(Position.X, work.Right - width));
        int y = Math.Max(work.Y, Math.Min(Position.Y, work.Bottom - height));
        if (x != Position.X || y != Position.Y)
        {
            Position = new PixelPoint(x, y);
        }
    }

    // The control that had the keyboard, when it is still on screen; else the opener.
    private static void GiveFocusBack(IInputElement? before, Visual? opener)
    {
        if (before is InputElement element && TopLevel.GetTopLevel(element) is not null && element.IsEffectivelyVisible && element.Focusable)
        {
            element.Focus();
        }
        else if (opener is InputElement { Focusable: true } fallback && TopLevel.GetTopLevel(fallback) is not null)
        {
            fallback.Focus();
        }
    }
}
