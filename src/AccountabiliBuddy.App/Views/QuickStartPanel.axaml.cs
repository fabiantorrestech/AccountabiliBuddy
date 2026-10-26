using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The start controls shared by the quick-session popup and the Session tab. When the end-time editor opens the
/// cursor goes to its time box, and back to the window when it closes. Shift is tracked here, so the slider moves
/// in 5-minute steps while it is held wherever the panel is shown.
/// </summary>
public partial class QuickStartPanel : UserControl
{
    private QuickSessionViewModel? _subscribed;

    public QuickStartPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeToViewModel();

        // Tunnel so Shift is seen wherever focus is inside the panel. Shift may have been pressed before the window
        // had focus; pointer events carry the current modifiers.
        AddHandler(PointerPressedEvent, OnPreviewPointer, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPreviewPointer, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnPreviewShiftKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewShiftKey, RoutingStrategies.Tunnel);
    }

    /// <summary>True while the profile list is open (it opens in its own popup window).</summary>
    public bool IsProfileDropDownOpen => ProfileBox.IsDropDownOpen;

    private void OnPreviewPointer(object? sender, PointerEventArgs e)
    {
        if (DataContext is QuickSessionViewModel viewModel)
        {
            viewModel.ShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        }
    }

    private void OnPreviewShiftKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is QuickSessionViewModel viewModel && e.Key is Key.LeftShift or Key.RightShift)
        {
            viewModel.ShiftHeld = e.RoutedEvent == KeyDownEvent;
        }
    }

    private void SubscribeToViewModel()
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = DataContext as QuickSessionViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QuickSessionViewModel.IsEditingUntil))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_subscribed?.IsEditingUntil == true)
            {
                UntilBox.Focus();
                UntilBox.SelectAll();
            }
            else
            {
                TopLevel.GetTopLevel(this)?.Focus();
            }
        });
    }
}
