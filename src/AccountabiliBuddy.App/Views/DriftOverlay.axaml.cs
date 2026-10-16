using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

public partial class DriftOverlay : Window
{
    public DriftOverlay()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Alt+F4 must not get rid of the overlay; only the app closes it (or app shutdown).
        if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
