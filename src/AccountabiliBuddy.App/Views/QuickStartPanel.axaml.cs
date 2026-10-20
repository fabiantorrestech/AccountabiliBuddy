using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The start controls shared by the quick-session popup and the Session tab. When the end-time editor opens the
/// cursor goes to its time box, and back to the window when it closes.
/// </summary>
public partial class QuickStartPanel : UserControl
{
    private QuickSessionViewModel? _subscribed;

    public QuickStartPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeToViewModel();
    }

    /// <summary>True while the profile list is open (it opens in its own popup window).</summary>
    public bool IsProfileDropDownOpen => ProfileBox.IsDropDownOpen;

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
