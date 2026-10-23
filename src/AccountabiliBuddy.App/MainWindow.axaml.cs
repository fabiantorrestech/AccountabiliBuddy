using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services.Search;
using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AccountabiliBuddy.App;

public partial class MainWindow : Window
{
    // Pause between warming two pages, so clicks and typing stay responsive meanwhile.
    private static readonly TimeSpan WarmUpGap = TimeSpan.FromMilliseconds(300);

    // How long a found section's card stays outlined.
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(1400);
    private static readonly IBrush FoundBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21));

    private bool _warmedUp;

    private int _shownTab;

    /// <summary>Files dropped on the window (sync files or pictures of sync codes, ADR 22), by their local paths.</summary>
    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += (_, _) => WarmUpPages();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.HideRequested += (_, _) => Hide();
                ConnectSearch(viewModel);
            }
        };
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        List<string> paths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => item.TryGetLocalPath())
            .OfType<string>()
            .ToList();
        if (paths.Count > 0)
        {
            FilesDropped?.Invoke(this, paths);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Search.FocusBox();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Wires the search box to the pages: highlights on the page showing, its words for "Search All Results…", and
    /// opening a result (the tab, then the profile, date or Settings group, then the control, whose card flashes).
    /// </summary>
    private void ConnectSearch(MainWindowViewModel viewModel)
    {
        if (viewModel.Search is not SearchViewModel search)
        {
            return;
        }

        Highlights.Page = Pages.Children[Math.Max(0, Tabs.SelectedIndex)];
        search.UiEntries = SearchPages;
        search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.Query))
            {
                Highlights.Query = search.Query;
            }
        };
        Highlights.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchHighlightLayer.MatchCountProperty)
            {
                search.PageCount = Highlights.MatchCount;
            }
            else if (e.Property == SearchHighlightLayer.CurrentIndexProperty)
            {
                search.PageIndex = Highlights.CurrentIndex;
            }
        };
        search.NextRequested += (_, _) => Highlights.Next();
        search.PreviousRequested += (_, _) => Highlights.Previous();

        var navigator = new SearchNavigator(
            tab => viewModel.SelectedTab = tab,
            profileId => viewModel.Profiles.Select(profileId),
            date => viewModel.Exceptions.ShowDate(date),
            group => viewModel.Settings.SelectedGroupIndex = group,
            template => viewModel.DayPlan.SelectedTemplateIndex = template,
            control => Dispatcher.UIThread.Post(() => RevealFound(control as Control), DispatcherPriority.Background));
        search.OpenRequested += (_, target) =>
        {
            navigator.Open(target);
            // Data results have no control: the page's own matches show once it's laid out.
            Dispatcher.UIThread.Post(Highlights.RecountNow, DispatcherPriority.Background);
        };
    }

    /// <summary>Every page's words (the dev tab left out), with its tab name.</summary>
    private IReadOnlyList<SearchEntry> SearchPages()
    {
        var pages = new List<(int Tab, string TabName, Control Page)>();
        for (int i = 0; i <= SearchTabs.About && i < Pages.Children.Count; i++)
        {
            if (Tabs.Items[i] is TabItem { Header: string name })
            {
                pages.Add((i, name, Pages.Children[i]));
            }
        }

        return UiSearchSource.Entries(pages);
    }

    /// <summary>Brings a found control into view and flashes the card it sits in.</summary>
    private void RevealFound(Control? control)
    {
        if (control is null)
        {
            return;
        }

        control.BringIntoView();
        if (control.GetLogicalAncestors().OfType<Border>().FirstOrDefault(border => SearchArea.GetName(border) is not null) is Border card)
        {
            card.BorderBrush = FoundBrush;
            DispatcherTimer.RunOnce(() => card.ClearValue(Border.BorderBrushProperty), FlashTime);
        }

        Highlights.RecountNow();
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Only the tab strip's own change; a ComboBox inside a page raises this event too.
        if (!ReferenceEquals(e.Source, Tabs))
        {
            return;
        }

        // "More" only unfolds the tabs under it; the view model puts the selection back on the page showing.
        if (Tabs.SelectedIndex == (int)MainTab.More)
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

        // Search highlights follow the page showing.
        Highlights.Page = Pages.Children[_shownTab];
        Dispatcher.UIThread.Post(Highlights.RecountNow, DispatcherPriority.Background);
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
