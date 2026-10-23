using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The capture popup (Ctrl+Alt+N): one line. Enter saves it, Esc drops it, and clicking away saves what's typed, so an idea
/// is never lost on the way.
/// </summary>
public partial class CapturePopup : Window
{
    private bool _dropped;
    private bool _saved;

    public CapturePopup()
    {
        InitializeComponent();
        Opened += (_, _) => Dispatcher.UIThread.Post(() => Line.Focus());
        Deactivated += (_, _) => Close();
        Closing += (_, _) => SaveOnce();
    }

    /// <summary>Raised with the text when it's saved (Enter or clicking away), never for Esc.</summary>
    public event EventHandler<string>? Saved;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _dropped = true;
            Close();
            e.Handled = true;
        }
    }

    private void SaveOnce()
    {
        if (_saved || _dropped)
        {
            return;
        }

        _saved = true;
        string text = Line.Text ?? string.Empty;
        if (text.Trim().Length > 0)
        {
            Saved?.Invoke(this, text);
        }
    }
}
