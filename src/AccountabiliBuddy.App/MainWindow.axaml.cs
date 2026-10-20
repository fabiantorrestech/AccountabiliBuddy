using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App;

public partial class MainWindow : Window
{
    // Pause between warming two pages, so clicks and typing stay responsive meanwhile.
    private static readonly TimeSpan WarmUpGap = TimeSpan.FromMilliseconds(300);

    private bool _warmedUp;

    private int _shownTab;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WarmUpPages();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.HideRequested += (_, _) => Hide();
            }
        };
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Only the tab strip's own change; a ComboBox inside a page raises this event too.
        if (!ReferenceEquals(e.Source, Tabs))
        {
            return;
        }

        // Settings with unsaved changes: stay there while it asks Save / Discard / Stay.
        if (DataContext is MainWindowViewModel viewModel && !viewModel.MayLeaveTab(_shownTab, Tabs.SelectedIndex))
        {
            int back = _shownTab;
            Dispatcher.UIThread.Post(() => Tabs.SelectedIndex = back);
            return;
        }

        _shownTab = Tabs.SelectedIndex;

        for (int i = 0; i < Pages.Children.Count; i++)
        {
            Pages.Children[i].IsVisible = i == Tabs.SelectedIndex;
        }
    }

    /// <summary>
    /// Lays out each hidden page once, one at a time while the app is idle, so even the first visit to a tab is
    /// instant. Each page is shown, laid out and hidden again within one step, so it never appears on screen.
    /// </summary>
    private void WarmUpPages()
    {
        if (_warmedUp)
        {
            return;
        }

        _warmedUp = true;
        WarmUpPage(0);
    }

    private void WarmUpPage(int index)
    {
        if (index >= Pages.Children.Count)
        {
            return;
        }

        Control page = Pages.Children[index];
        if (!page.IsVisible)
        {
            page.Opacity = 0;
            page.IsVisible = true;
            Pages.UpdateLayout();
            page.IsVisible = false;
            page.Opacity = 1;
        }

        DispatcherTimer.RunOnce(
            () => Dispatcher.UIThread.Post(() => WarmUpPage(index + 1), DispatcherPriority.Background),
            WarmUpGap);
    }

    /// <summary>When false, closing the window only hides it to the tray.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            if (DataContext is MainWindowViewModel viewModel && !viewModel.MayLeaveTab(_shownTab, null))
            {
                return; // It asks first; Save or Discard then hides it.
            }

            Hide();
        }

        base.OnClosing(e);
    }
}
