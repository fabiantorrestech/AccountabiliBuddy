using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

public partial class HistoryView : UserControl
{
    private HistoryViewModel? _viewModel;

    public HistoryView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = DataContext as HistoryViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }
    }

    // The add form sits under the totals and lists; when it opens (e.g. from a click on the day bar) it scrolls into view.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.IsAddFormOpen) && _viewModel?.IsAddFormOpen == true)
        {
            Dispatcher.UIThread.Post(() => AddForm.BringIntoView(), DispatcherPriority.Loaded);
        }
    }
}
