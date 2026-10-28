using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Plan page. Code-behind: Esc and a click outside close the sheet, the sheet's width follows the page, the page starts
/// at its top when shown and when the day changes, and while the page is on screen in a window that is showing, a
/// once-a-minute tick moves the "Now" line (the helper itself never runs on a timer).
/// </summary>
public partial class PlanView : UserControl
{
    private const double DayTypesSheetWidth = 700;
    private const double ChangeADaySheetWidth = 980;
    private const double SheetGap = 40;

    private PlanViewModel? _subscribed;
    private DispatcherTimer? _clock;
    private Window? _window;

    public PlanView()
    {
        InitializeComponent();
        Page.SizeChanged += (_, _) => UpdateSheetWidth();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnPagePropertyChanged;
            _subscribed.EditorRequested -= OnEditorRequested;
        }

        _subscribed = DataContext as PlanViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnPagePropertyChanged;
            _subscribed.EditorRequested += OnEditorRequested;
        }

        UpdateSheetWidth();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
        }

        UpdateClock(shown: false);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }

        _clock?.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            UpdateClock(shown: IsVisible);
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty)
        {
            UpdateClock(shown: false);
        }
    }

    // The tick runs only while this page is showing in a window that is visible and not minimized; it catches up when it restarts.
    private void UpdateClock(bool shown)
    {
        bool run = IsVisible && _window is { IsVisible: true, WindowState: not WindowState.Minimized };
        if (!run)
        {
            _clock?.Stop();
            return;
        }

        if (shown)
        {
            (DataContext as PlanViewModel)?.OnShown();
            MainScroll.ScrollToHome();
        }
        else if (_clock?.IsEnabled != true)
        {
            (DataContext as PlanViewModel)?.Tick();
        }

        if (_clock is null)
        {
            _clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _clock.Tick += (_, _) => (DataContext as PlanViewModel)?.Tick();
        }

        _clock.Start();
    }

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlanViewModel.Sheet))
        {
            UpdateSheetWidth();
            if (_subscribed?.IsSheetOpen == false)
            {
                MainScroll.ScrollToHome();
            }
        }
        else if (e.PropertyName == nameof(PlanViewModel.SelectedDate))
        {
            MainScroll.ScrollToHome();
        }
    }

    /// <summary>
    /// A search result lies in one of the sheets (their words are indexed even while closed): opens that sheet, so the result
    /// can be brought into view and highlighted.
    /// </summary>
    public void OpenSheetContaining(Control control)
    {
        if (IsWithin(control, DayTypesView))
        {
            _subscribed?.OpenDayTypes();
        }
        else if (IsWithin(control, ChangeADayView))
        {
            _subscribed?.OpenChangeADay();
        }
    }

    private static bool IsWithin(Control control, Control root)
    {
        return control == root || control.GetLogicalAncestors().Contains(root);
    }

    // The sheet is as wide as its content likes, but never wider than the page less a gap that keeps the page behind it visible.
    private void UpdateSheetWidth()
    {
        double wanted = _subscribed?.IsChangeADayOpen == true ? ChangeADaySheetWidth : DayTypesSheetWidth;
        Sheet.Width = Math.Max(320, Math.Min(wanted, Page.Bounds.Width - SheetGap));
    }

    // The sheet is laid out first, then its editor scrolls into view.
    private void OnEditorRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_subscribed?.IsDayTypesOpen == true)
                {
                    DayTypesView.ShowTemplateEditor();
                }
                else
                {
                    ChangeADayView.ShowOneOffEditor();
                }
            },
            DispatcherPriority.Background);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        _subscribed?.CloseSheet();
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _subscribed?.IsSheetOpen == true)
        {
            _subscribed.CloseSheet();
            e.Handled = true;
        }
    }
}
