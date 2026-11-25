using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Archive sheet's content. Code-behind: lays the columns out like the Shelf's (6.13, 6.15-6.17): the columns its pill shows, an
/// open one sharing the width with the other open ones, a folded one a 62 px rail, 14 px apart; a hidden one takes no room.
/// </summary>
public partial class NotebookView : UserControl
{
    private const double RailWidth = 62;

    private NotebookViewModel? _subscribed;

    // 7.11: the panel's gap under the Archive's search box, and the room the sheet keeps below it ("66% of the card − 76").
    private const double FiltersGap = 6;
    private const double SheetKeeps = 76;
    private const double SheetPadding = 24;

    public NotebookView()
    {
        InitializeComponent();
        FiltersLayer.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && FiltersLayer.IsVisible)
            {
                PlaceFilters();
                Dispatcher.UIThread.Post(PlaceFilters, DispatcherPriority.Loaded);
            }
        };
        FiltersLayer.SizeChanged += (_, _) => PlaceFilters();
    }

    // Under the search box, its right edge under the box's; at most the sheet's height − 76 tall and never past the sheet's bottom.
    private void PlaceFilters()
    {
        if (!FiltersLayer.IsVisible || FiltersLayer.Bounds.Height <= 0
            || SearchPill.TranslatePoint(new Point(0, SearchPill.Bounds.Height), FiltersLayer) is not Point below)
        {
            return;
        }

        double top = below.Y + FiltersGap;
        double sheetHeight = FiltersLayer.Bounds.Height + SheetPadding;
        Filters.PlaceIn(FiltersLayer, SearchPill, top, Math.Min(sheetHeight - SheetKeeps, FiltersLayer.Bounds.Height + 12 - top));
    }

    // A click on the sheet outside the panel closes it (and does nothing else).
    private void OnFiltersOutsidePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.IsSearchFiltersOpen = false;
            e.Handled = true;
        }
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
