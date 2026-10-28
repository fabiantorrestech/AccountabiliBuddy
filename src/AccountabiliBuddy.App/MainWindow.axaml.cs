using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services.Search;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App;

public partial class MainWindow : Window
{
    // Pause between warming two pages, so clicks and typing stay responsive meanwhile.
    private static readonly TimeSpan WarmUpGap = TimeSpan.FromMilliseconds(300);

    // How long a found section's card stays outlined.
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(1400);
    private static readonly IBrush FoundBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21));

    // The pointer rests on the sync light this long before its popover opens (a click opens it at once).
    private static readonly TimeSpan SyncHoverDelay = TimeSpan.FromMilliseconds(350);

    // The pages the search reads, by MainTab (the dev Activity page is left out).
    private static readonly string[] SearchedPageNames = ["Today", "Tasks", "Plan", "Stats", "Settings"];

    private readonly DispatcherTimer _syncHoverTimer = new() { Interval = SyncHoverDelay };

    private bool _warmedUp;

    private int _shownTab;

    /// <summary>Files dropped on the window (sync files or pictures of sync codes, ADR 22), by their local paths.</summary>
    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        // A click away from the search flyout closes it, unless it holds a search (its highlights are then still showing).
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // The maximize button shows "restore" while the window is maximized.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                MaximizeIcon.Kind = WindowState == WindowState.Maximized ? Material.Icons.MaterialIconKind.WindowRestore : Material.Icons.MaterialIconKind.CropSquare;
            }
        };
        SyncLightButton.PointerEntered += (_, _) => _syncHoverTimer.Start();
        SyncLightButton.PointerExited += (_, _) => _syncHoverTimer.Stop();
        _syncHoverTimer.Tick += (_, _) =>
        {
            _syncHoverTimer.Stop();
            if (SyncLightButton.Flyout is { IsOpen: false } popover)
            {
                popover.ShowAt(SyncLightButton);
            }
        };
        Search.CloseRequested += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.IsSearchOpen = false;
            }
        };
        Opened += (_, _) => WarmUpPages();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.HideRequested += (_, _) => Hide();
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
                    {
                        OnSelectedTabChanged(viewModel);
                    }
                    else if (e.PropertyName == nameof(MainWindowViewModel.IsSearchOpen) && viewModel.IsSearchOpen)
                    {
                        Dispatcher.UIThread.Post(Search.FocusBox, DispatcherPriority.Loaded);
                    }
                };
                // The sync light's popover is its own little tree; it reads the window's view model.
                SyncPopover.DataContext = viewModel;
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

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { IsSearchOpen: true, Search: { } search } viewModel || search.HasQuery)
        {
            return;
        }

        if (e.Source is Visual clicked && (IsInside(SearchFlyout, clicked) || IsInside(SearchButton, clicked)))
        {
            return;
        }

        viewModel.IsSearchOpen = false;
    }

    private static bool IsInside(Visual container, Visual visual)
    {
        return ReferenceEquals(container, visual) || container.IsVisualAncestorOf(visual);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.IsSearchOpen = true;
            Search.FocusBox();
            e.Handled = true;
        }
        else if (e.Key == Key.R && e.KeyModifiers == KeyModifiers.Control)
        {
            // The same as the title bar's sync button.
            if (viewModel.RefreshNowCommand.CanExecute(null))
            {
                viewModel.RefreshNowCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape && viewModel.IsSearchOpen)
        {
            viewModel.Search?.ClearCommand.Execute(null);
            viewModel.IsSearchOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Wires the search box to the pages: highlights on the page showing, its words for "Search All Results…", and
    /// opening a result (the page, then the Settings row, profile or date, then the control, whose card flashes).
    /// </summary>
    private void ConnectSearch(MainWindowViewModel viewModel)
    {
        if (viewModel.Search is not SearchViewModel search)
        {
            return;
        }

        Highlights.Page = Pages.Children[Math.Max(0, _shownTab)];
        search.UiEntries = SearchPages;
        // Settings' own Search field finds rows by the same words.
        viewModel.Settings.RowsMatching = query => SearchPages()
            .Where(entry => entry.Target is { Tab: SearchTabs.Settings, SettingsGroup: not null } && TextMatcher.Find(entry.Text, query).Count > 0)
            .Select(entry => entry.Target.SettingsGroup!.Value)
            .ToHashSet();
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
            date => viewModel.Plan?.OpenChangeADay(date),
            row => viewModel.Settings.SelectedRowIndex = row,
            template => viewModel.Plan?.OpenDayTypes(template),
            control => Dispatcher.UIThread.Post(() => RevealFound(control as Control), DispatcherPriority.Background));
        search.OpenRequested += (_, target) =>
        {
            navigator.Open(target);
            // Words inside the Plan page's sheets: open the sheet that holds them, then the reveal below finds them on screen.
            if (target.Control is Control found && Pages.Children[(int)MainTab.Plan] is PlanView plan)
            {
                plan.OpenSheetContaining(found);
            }

            // Data results have no control: the page's own matches show once it's laid out.
            Dispatcher.UIThread.Post(Highlights.RecountNow, DispatcherPriority.Background);
        };
    }

    /// <summary>Every page's words (the dev page left out), with its page name.</summary>
    private IReadOnlyList<SearchEntry> SearchPages()
    {
        var pages = new List<(int Tab, string TabName, Control Page)>();
        for (int i = 0; i < SearchedPageNames.Length && i < Pages.Children.Count; i++)
        {
            pages.Add((i, SearchedPageNames[i], Pages.Children[i]));
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

    private void OnSelectedTabChanged(MainWindowViewModel viewModel)
    {
        int selected = viewModel.SelectedTab;
        if (selected == _shownTab)
        {
            return;
        }

        // Settings with unsaved changes: stay there while it asks Save / Discard / Stay.
        if (!viewModel.MayLeaveTab(_shownTab, selected))
        {
            int back = _shownTab;
            Dispatcher.UIThread.Post(() => viewModel.SelectedTab = back);
            return;
        }

        _shownTab = selected;

        for (int i = 0; i < Pages.Children.Count; i++)
        {
            Pages.Children[i].IsVisible = i == selected;
        }

        // The page coming into view fades and lifts in (Settings → Animations).
        AccountabiliBuddy.App.Services.Motion.Current?.Enter(Pages.Children[_shownTab]);

        // Search highlights follow the page showing.
        Highlights.Page = Pages.Children[_shownTab];
        Dispatcher.UIThread.Post(Highlights.RecountNow, DispatcherPriority.Background);
    }

    /// <summary>
    /// Lays out each hidden page once (and each Settings row), one at a time while the app is idle, so even the first
    /// visit to a page is instant. Each is shown, laid out and hidden again within one step, so it never appears on screen.
    /// </summary>
    private void WarmUpPages()
    {
        if (_warmedUp)
        {
            return;
        }

        _warmedUp = true;
        var steps = new Queue<Action>();
        for (int i = 0; i < Pages.Children.Count; i++)
        {
            int index = i;
            steps.Enqueue(() => WarmUpPage(index));
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            // Row 0 is laid out with the Settings page; the rest are built here, so a first visit to Guards or Devices is instant.
            for (int row = 1; row < viewModel.Settings.Rows.Count; row++)
            {
                int index = row;
                steps.Enqueue(() => WarmUpSettingsRow(viewModel, index));
            }
        }

        RunWarmUp(steps);
    }

    private void RunWarmUp(Queue<Action> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        steps.Dequeue()();
        DispatcherTimer.RunOnce(() => Dispatcher.UIThread.Post(() => RunWarmUp(steps), DispatcherPriority.Background), WarmUpGap);
    }

    private void WarmUpPage(int index)
    {
        Control page = Pages.Children[index];
        if (!page.IsVisible)
        {
            page.Opacity = 0;
            page.IsVisible = true;
            Pages.UpdateLayout();
            page.IsVisible = false;
            page.Opacity = 1;
        }
    }

    private void WarmUpSettingsRow(MainWindowViewModel viewModel, int row)
    {
        Control page = Pages.Children[(int)MainTab.Settings];
        if (page.IsVisible || viewModel.ShowsLeavePrompt)
        {
            return; // The user is there; what they look at is built already.
        }

        int original = viewModel.Settings.SelectedRowIndex;
        page.Opacity = 0;
        page.IsVisible = true;
        viewModel.Settings.SelectedRowIndex = row;
        Pages.UpdateLayout();
        viewModel.Settings.SelectedRowIndex = original;
        page.IsVisible = false;
        page.Opacity = 1;
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
