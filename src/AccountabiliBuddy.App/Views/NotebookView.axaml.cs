using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Archive sheet's content. Code-behind: lays the columns out like the Shelf's (6.13, 6.15-6.17): the columns its pill shows, an
/// open one sharing the width with the other open ones, a folded one a 62 px rail, 14 px apart; a hidden one takes no room.
/// </summary>
public partial class NotebookView : UserControl
{
    private const double RailWidth = 62;

    private NotebookViewModel? _subscribed;

    public NotebookView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            foreach (ArchiveColumnViewModel column in _subscribed.Columns)
            {
                column.PropertyChanged -= OnColumnChanged;
            }
        }

        _subscribed = DataContext as NotebookViewModel;
        if (_subscribed is not null)
        {
            foreach (ArchiveColumnViewModel column in _subscribed.Columns)
            {
                column.PropertyChanged += OnColumnChanged;
            }
        }

        LayoutColumns();
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArchiveColumnViewModel.IsShown) or nameof(ArchiveColumnViewModel.IsFolded))
        {
            LayoutColumns();
        }
    }

    // One grid column per shown Archive column, in order; the spacing then falls only between the ones that show.
    private void LayoutColumns()
    {
        if (_subscribed is null)
        {
            return;
        }

        (ArchiveColumnView View, ArchiveColumnViewModel Column)[] all =
        [
            (UnfinishedView, _subscribed.UnfinishedColumn),
            (DoneView, _subscribed.DoneColumn),
            (DeletedView, _subscribed.DeletedColumn),
        ];
        ColumnsGrid.ColumnDefinitions.Clear();
        foreach ((ArchiveColumnView view, ArchiveColumnViewModel column) in all)
        {
            view.IsVisible = column.IsShown;
            if (!column.IsShown)
            {
                Grid.SetColumn(view, 0);
                continue;
            }

            Grid.SetColumn(view, ColumnsGrid.ColumnDefinitions.Count);
            ColumnsGrid.ColumnDefinitions.Add(column.IsFolded ? new ColumnDefinition(RailWidth, GridUnitType.Pixel) : new ColumnDefinition(1, GridUnitType.Star));
        }
    }
}
