using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Plan page. Code-behind: Esc and a click outside close the sheet, the sheet's width follows the page, and while the page
/// is on screen a once-a-minute tick moves the "Now" line (the helper itself never runs on a timer).
/// </summary>
public partial class PlanView : UserControl
{
    private const double DayTypesSheetWidth = 700;
    private const double ChangeADaySheetWidth = 980;
    private const double SheetGap = 40;

    private PlanViewModel? _subscribed;
    private DispatcherTimer? _clock;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                StartClock();
            }
            else
            {
                _clock?.Stop();
            }
        }
    }

    private void StartClock()
    {
        (DataContext as PlanViewModel)?.OnShown();
        MainScroll.ScrollToHome();
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
        }
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
