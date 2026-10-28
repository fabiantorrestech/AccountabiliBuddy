using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

public partial class TodayView : UserControl
{
    private SessionViewModel? _subscribed;

    public TodayView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnSessionPropertyChanged;
        }

        _subscribed = DataContext as SessionViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnSessionPropertyChanged;
        }
    }

    // The main card changed state (idle, planning, running, shutdown): the page goes back to its top, so the new state isn't cut off
    // by the old scroll offset. After layout, once the new extent is known.
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.CardState))
        {
            Dispatcher.UIThread.Post(() => PageScroll.ScrollToHome(), DispatcherPriority.Background);
        }
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
