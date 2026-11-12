using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Today sidebar (<see cref="TodaySidebarViewModel"/>). Code-behind: the slide follows the Motion setting (Off: it shows and hides
/// at once; the slide is otherwise 220 ms, eased), and editing a come-back takes the keyboard.
/// </summary>
public partial class TodaySidebarView : UserControl
{
    private const string HiddenClass = "hidden";

    // A little longer than the slide (220 ms) and the fade (180 ms), so the panel is drawn until it is out of sight.
    private static readonly TimeSpan SlideOut = TimeSpan.FromMilliseconds(260);

    private readonly DispatcherTimer _hideTimer = new() { Interval = SlideOut };
    private Transitions? _panelTransitions;
    private TodaySidebarViewModel? _viewModel;

    public TodaySidebarView()
    {
        InitializeComponent();
        // Round 2 review: a hidden panel leaves the Tab order and stops drawing once it has slid out, so Tab in a session can't land
        // on an invisible button (IsEnabled follows IsOpen at once; IsVisible goes false after the slide, true again before one).
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_viewModel is not { IsOpen: true })
            {
                SidebarPanel.IsVisible = false;
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as TodaySidebarViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        // The first state shows at once (no slide).
        _hideTimer.Stop();
        bool open = _viewModel?.IsOpen == true;
        SidebarPanel.Classes.Set(HiddenClass, !open);
        SidebarPanel.IsVisible = open;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodaySidebarViewModel.IsOpen))
        {
            ApplyOpen(_viewModel?.IsOpen == true);
        }
    }

    // Opening: drawn first, then the "hidden" class goes on the next pass so the slide runs from out of sight. Closing: the class goes
    // on now (the slide-out runs) and the panel stops being drawn once it is over; with Motion off, at once.
    private void ApplyOpen(bool open)
    {
        _hideTimer.Stop();
        if (open)
        {
            SidebarPanel.IsVisible = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel is { IsOpen: true })
                {
                    SidebarPanel.Classes.Set(HiddenClass, false);
                }
            }, DispatcherPriority.Render);
            return;
        }

        SidebarPanel.Classes.Set(HiddenClass, true);
        if (Services.Motion.Current is { IsOn: false })
        {
            SidebarPanel.IsVisible = false;
        }
        else
        {
            _hideTimer.Start();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyMotion();
        Services.Motion.SpeedChanged += OnMotionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Services.Motion.SpeedChanged -= OnMotionChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        _panelTransitions = Services.Motion.Gate(SidebarPanel, _panelTransitions);
    }

    // Editing a come-back: the box takes the keyboard as it opens, with the old text selected.
    private void OnComebackBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    // Clicking away keeps the new text, like Enter; Escape has already closed the box, so nothing is saved then.
    private void OnComebackBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ComebackRowViewModel { IsEditing: true } row })
        {
            row.SaveEditCommand.Execute(null);
        }
    }
}
