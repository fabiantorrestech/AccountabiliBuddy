using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Navigation;
using AccountabiliBuddy.App.Services.Search;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    // The pages the search reads, by MainTab (the dev Activity page is left out).
    private static readonly string[] SearchedPageNames = ["Today", "Tasks", "Plan", "Stats", "Settings"];

    // The Tasks keyboard map's window keys (Ctrl+G, Ctrl+Z, Del, F2, H, Esc's last steps).
    private readonly TaskShortcuts _taskShortcuts;

    private void OnLunchChipPromptClosed(object? sender, EventArgs e) => LunchChipEndButton.Flyout?.Hide();

    private bool _warmedUp;
    private bool _hookedWindowMessages;

    // Back / forward's page slide (NAV2): the run in flight, and true while it puts the place back (the tab's own fade is then skipped).
    private HistorySlideRun? _historySlide;
    private bool _historyMoving;

    // The two-finger swipe (NAV2): its tracker, a clock for it, the pause timer that ends a swipe, and whether this swipe is off
    // (it began over something that scrolls sideways, or while a dialog was open).
    private static readonly TimeSpan SwipeBubbleHold = TimeSpan.FromMilliseconds(160);
    private readonly SwipeTracker _swipe = new();
    private readonly System.Diagnostics.Stopwatch _swipeClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly DispatcherTimer _swipeTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _swipeBubbleHide = new() { Interval = SwipeBubbleHold };
    private TimeSpan _lastWheelAt;
    private bool _swipeOff;

    // The title bar's pop-ups (volume, session menu, lunch prompt, and the title-bar menu): opening one closes the others (round 2, brief
    // §4.2; Tasks redesign 9.06). The menu is a panel in the window rather than a flyout, so it joins the rule by hand: it closes when one
    // of these opens, and opening it hides them.
    private readonly OnePopupAtATime _titleBarPopups = new();
    private readonly List<FlyoutBase> _titleBarFlyouts = [];

    // The title-bar menu's view model (Tasks redesign 9), made when the window gets its view model.
    private TitleMenuViewModel? _titleMenu;

    private int _shownTab;

    /// <summary>Files dropped on the window (sync files or pictures of sync codes, ADR 22), by their local paths.</summary>
    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    public MainWindow()
    {
        InitializeComponent();
        // Styles that move only while Animations is on (the sync spin, Tasks' reset fade) look for .motionOn on the window.
        Classes.Set("motionOn", Services.Motion.Current is not { IsOn: false });
        Services.Motion.SpeedChanged += (_, _) => Classes.Set("motionOn", Services.Motion.Current is not { IsOn: false });
        // Ctrl+G's prompt opens under the title-bar menu's button (where the search lives).
        _taskShortcuts = new TaskShortcuts(tasks => GoToTaskPopover.ShowAt(TitleMenuButton, tasks.GoToTask));
        // Del's key repeats are ignored until it comes up (seen even when something handled it), or the window loses the keyboard.
        AddHandler(KeyUpEvent, (_, e) => _taskShortcuts.KeyReleased(e.Key), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => _taskShortcuts.Reset();
        AddHandler(DragDrop.DropEvent, OnDrop);
        // A click away from the title-bar menu closes it, whatever it holds.
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // The mouse's back / forward buttons, anywhere in the window (owner 2026-11-14).
        AddHandler(PointerPressedEvent, OnNavigationPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // A touchpad's two-finger swipe arrives as horizontal wheel events (NAV2). Handled ones too: a page that scrolls up and down
        // takes the swipe's small vertical part and marks the event handled, and the swipe must still count; sideways scrollers
        // are left to themselves by HorizontalScrollGuard instead.
        AddHandler(PointerWheelChangedEvent, OnSwipeWheel, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _swipeTimer.Tick += (_, _) => OnSwipeTick();
        _swipeBubbleHide.Tick += (_, _) =>
        {
            _swipeBubbleHide.Stop();
            SwipeLayer.IsVisible = false;
        };
        Deactivated += (_, _) => ResetSwipe();
        // The maximize button shows "restore" while the window is maximized.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                MaximizeIcon.Kind = WindowState == WindowState.Maximized ? Material.Icons.MaterialIconKind.WindowRestore : Material.Icons.MaterialIconKind.CropSquare;
            }
        };
        // The buttons' ElementRole makes Windows hit-test them as caption buttons (snap layouts), but the clicks arrive as
        // pointer events and nothing acts on them by itself: Avalonia's own decorations wire these same three actions.
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close(); // OnClosing hides to the tray unless AllowClose is set.
        TitleMenuButton.Click += (_, _) => ToggleTitleMenu();
        TitleMenu.CloseRequested += (_, _) => CloseTitleMenu();
        // The dropdown stays right-aligned to its button as the window is resized.
        Root.SizeChanged += (_, _) =>
        {
            if (TitleMenu.IsVisible)
            {
                PlaceTitleMenu();
            }
        };

        // A session started or ended with Animations Off: the session control's room changed at once, so the left group (the sound
        // name it narrowed or widened) fades in rather than jumping (owner 2026-11-14).
        SessionControl.FootprintSnapped += (_, _) => FadeInTitleBarStart();
        AddTitleBarPopup(SessionControl.SessionMenu);
        AddTitleBarPopup(NoiseControl.VolumeFlyout);
        // The lunch chip's "End lunch?" pop-up (LU1b): it reads lunch fresh when it opens and closes itself when answered.
        if (LunchChipEndButton.Flyout is FlyoutBase lunchFlyout)
        {
            AddTitleBarPopup(lunchFlyout);
            lunchFlyout.Opened += (_, _) =>
            {
                if (DataContext is MainWindowViewModel { Session.Lunch: { } card })
                {
                    card.TitleBarPrompt.Closed -= OnLunchChipPromptClosed;
                    card.TitleBarPrompt.Closed += OnLunchChipPromptClosed;
                    card.TitleBarPrompt.Open();
                }
            };
            lunchFlyout.Closed += (_, _) =>
            {
                if (DataContext is MainWindowViewModel { Session.Lunch: { } card })
                {
                    card.TitleBarPrompt.IsOpen = false;
                }
            };
        }

        Opened += (_, _) => WarmUpPages();
        Deactivated += (_, _) => CloseTitleMenu(); // A click in another app is a click away too.
        Opened += (_, _) =>
        {
            if (!_hookedWindowMessages)
            {
                _hookedWindowMessages = true;
                Win32Properties.AddWndProcHookCallback(this, OnWindowMessage);
            }
        };
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
                    else if (e.PropertyName == nameof(MainWindowViewModel.IsSearchOpen))
                    {
                        ShowTitleMenu(viewModel.IsSearchOpen);
                    }
                };
                // The title-bar menu reads the window's search, sync light, refresh and theme; its folds are this PC's (document "syncLight").
                _titleMenu = new TitleMenuViewModel(viewModel, viewModel.Devices?.SyncLightSetting);
                TitleMenu.DataContext = _titleMenu;
                TitleMenuButton.DataContext = _titleMenu;
                ConnectSearch(viewModel);
                // Back / forward: no move while a dialog this window owns is open (the unsaved-settings prompt is checked by the view model).
                viewModel.IsModalOpen = () => OwnedWindows.Any(window => window.IsVisible);
                // Back / forward play the page slide (NAV2); a press during a slide ends it at once first.
                viewModel.HistoryNavigating += OnHistoryNavigating;
                viewModel.FinishHistoryMove = () => _historySlide?.FinishNow();
                viewModel.ConnectNavigation();
            }
        };
    }

    // ---- The title-bar menu (Tasks redesign 9) ----

    // A pop-up of the title bar's group: it closes the menu when it opens (the menu hides it when the menu opens).
    private void AddTitleBarPopup(FlyoutBase? popup)
    {
        if (popup is null)
        {
            return;
        }

        _titleBarPopups.Add(popup);
        _titleBarFlyouts.Add(popup);
        popup.Opened += (_, _) => CloseTitleMenu();
    }

    private void ToggleTitleMenu()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsSearchOpen = !viewModel.IsSearchOpen;
        }
    }

    /// <summary>Opens the title-bar menu with the caret in its search (a click on its button, or Ctrl+F off the Tasks page).</summary>
    private void OpenTitleMenu()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsSearchOpen)
        {
            TitleMenu.FocusSearch();
            return;
        }

        viewModel.IsSearchOpen = true;
    }

    private void CloseTitleMenu()
    {
        if (DataContext is MainWindowViewModel { IsSearchOpen: true } viewModel)
        {
            viewModel.IsSearchOpen = false;
        }
    }

    // The window's view model says open or closed: the dropdown shows (fading and dropping in over 0.16 s while Animations is on) or hides.
    private void ShowTitleMenu(bool open)
    {
        TitleMenuButton.Classes.Set("open", open);
        if (!open)
        {
            TitleMenu.IsVisible = false;
            if (TitleMenu.IsKeyboardFocusWithin)
            {
                TitleMenuButton.Focus();
            }

            return;
        }

        foreach (FlyoutBase popup in _titleBarFlyouts.Where(popup => popup.IsOpen))
        {
            popup.Hide();
        }

        PlaceTitleMenu();
        TitleMenu.Prepare();
        Avalonia.Animation.Transitions? transitions = TitleMenu.Transitions;
        TitleMenu.Transitions = null;
        TitleMenu.Opacity = transitions is null ? 1 : 0;
        TitleMenu.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(transitions is null ? "translateY(0px)" : "translateY(-6px)");
        TitleMenu.IsVisible = true;
        TitleMenu.Transitions = transitions;
        if (transitions is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                TitleMenu.Opacity = 1;
                TitleMenu.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px)");
            }, DispatcherPriority.Render);
        }

        Dispatcher.UIThread.Post(TitleMenu.FocusSearch, DispatcherPriority.Loaded);
    }

    // 8 under the button, its right edge on the button's (9.04); its height up to the window's bottom less 12, where it scrolls.
    private void PlaceTitleMenu()
    {
        Point? buttonRight = TitleMenuButton.TranslatePoint(new Point(TitleMenuButton.Bounds.Width, TitleMenuButton.Bounds.Height), Root);
        if (buttonRight is not Point corner)
        {
            return;
        }

        double right = Math.Max(0, Root.Bounds.Width - corner.X);
        TitleMenu.Margin = new Thickness(0, corner.Y + 8, right, 12);
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

    // A click away from the title-bar menu closes it, with or without a search typed (the words stay for next time). A click on its
    // button is left to the button, which toggles it.
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Visual? clickedVisual = e.Source as Visual;
        if (clickedVisual is not null && (IsInside(TitleMenu, clickedVisual) || IsInside(TitleMenuButton, clickedVisual)))
        {
            return;
        }

        CloseTitleMenu();
    }

    // A press on the title bar's empty part (or its edge) is a non-client click: Windows takes it for a drag and Avalonia
    // never sees it as a pointer press, so it is caught here.
    private nint OnWindowMessage(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        const uint WmNcLButtonDown = 0x00A1;
        if (msg == WmNcLButtonDown)
        {
            CloseTitleMenu();
        }

        return IntPtr.Zero;
    }

    private static readonly TimeSpan TitleBarFade = TimeSpan.FromMilliseconds(150);

    private DispatcherTimer? _titleBarFadeTimer;

    // A quick fade of the title bar's left group from faint to full (reduced motion: no movement, only opacity).
    private void FadeInTitleBarStart()
    {
        _titleBarFadeTimer?.Stop();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        TitleBarStart.Opacity = 0.3;
        _titleBarFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _titleBarFadeTimer.Tick += (_, _) =>
        {
            double t = Math.Clamp(clock.Elapsed / TitleBarFade, 0, 1);
            TitleBarStart.Opacity = 0.3 + 0.7 * t;
            if (t >= 1)
            {
                _titleBarFadeTimer?.Stop();
            }
        };
        _titleBarFadeTimer.Start();
    }

    // XButton1 / XButton2 go back / forward wherever the pointer is; the press is taken so nothing under it also acts on it.
    private void OnNavigationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (NavigationKeys.FromPointer(e.GetCurrentPoint(this).Properties.PointerUpdateKind) is HistoryDirection direction
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Go(direction);
            e.Handled = true;
        }
    }

    // ---- Back / forward's page slide ----

    // The old page slides out, the place is put back, the page now showing slides in. Animations Off: not handled, so the view model
    // shows the place at once (the tab's own fade is a no-op then too).
    private void OnHistoryNavigating(object? sender, HistoryNavigatingEventArgs e)
    {
        _historySlide?.FinishNow();
        if (Services.Motion.Current is not { IsOn: true } motion)
        {
            return;
        }

        e.Handled = true;
        _historySlide = motion.HistorySlide(Pages.Children[_shownTab], () =>
        {
            _historyMoving = true;
            try
            {
                e.Apply();
            }
            finally
            {
                _historyMoving = false;
            }

            return Pages.Children[_shownTab];
        }, e.Direction);
    }

    // ---- The two-finger swipe ----

    // True while something else owns the pointer or the page must not move: a dialog, the unsaved-settings prompt, the title-bar menu.
    private bool IsSwipeBlocked(MainWindowViewModel viewModel)
    {
        return viewModel.IsModalOpen?.Invoke() == true || viewModel.ShowsLeavePrompt || viewModel.IsSearchOpen;
    }

    private void OnSwipeWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        TimeSpan now = _swipeClock.Elapsed;
        if (now - _lastWheelAt >= SwipeTracker.IdleReset)
        {
            // A pause: a new swipe, with a fresh decision on whether it is allowed.
            _swipeOff = false;
        }

        _lastWheelAt = now;
        if (_swipeOff)
        {
            return;
        }

        bool horizontal = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y);
        if (horizontal && (IsSwipeBlocked(viewModel) || HorizontalScrollGuard.IsInHorizontalScroller(e.Source as Visual)))
        {
            // Over something that scrolls sideways (or with a dialog up) the swipe is off until the pause: the scrolling wins.
            _swipeOff = true;
            ResetSwipe();
            return;
        }

        // Settings › Touchpad swipe applies at once.
        _swipe.Style = Services.Motion.Current?.Settings.Swipe ?? SwipeStyle.Default;
        ShowSwipe(viewModel, _swipe.Feed(e.Delta.X, e.Delta.Y, now));
    }

    private void OnSwipeTick()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            ShowSwipe(viewModel, _swipe.Tick(_swipeClock.Elapsed));
        }
    }

    private void ShowSwipe(MainWindowViewModel viewModel, SwipeState state)
    {
        switch (state.Phase)
        {
            case SwipePhase.Tracking:
                _swipeBubbleHide.Stop();
                _swipeTimer.Start();
                DrawBubble(state, viewModel);
                break;
            case SwipePhase.Commit:
                // One move per swipe: the bubble shows full for a moment while the page slides.
                _swipeTimer.Stop();
                DrawBubble(state, viewModel);
                _swipeBubbleHide.Stop();
                _swipeBubbleHide.Start();
                viewModel.Go(state.Direction);
                break;
            case SwipePhase.Cancelled:
                _swipeTimer.Stop();
                SwipeLayer.IsVisible = false;
                break;
            default:
                _swipeTimer.Stop();
                break;
        }
    }

    private void ResetSwipe()
    {
        _swipe.Reset();
        _swipeTimer.Stop();
        _swipeBubbleHide.Stop();
        SwipeLayer.IsVisible = false;
    }

    // The bubble follows the swipe: out of the left edge going back, the right going forward; the accent at the threshold, greyed with nowhere to go.
    private void DrawBubble(SwipeState state, MainWindowViewModel viewModel)
    {
        bool available = state.Direction == HistoryDirection.Back ? viewModel.CanGoBack : viewModel.CanGoForward;
        SwipeBubblePlan plan = SwipeBubblePlan.For(state, available);
        bool back = plan.Direction == HistoryDirection.Back;
        SwipeBubble.HorizontalAlignment = back ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
        SwipeIcon.Kind = back ? Material.Icons.MaterialIconKind.ChevronLeft : Material.Icons.MaterialIconKind.ChevronRight;
        SwipeBubble.Classes.Set("filled", plan.Filled);
        SwipeBubble.Classes.Set("greyed", plan.Greyed);
        SwipeBubble.Opacity = plan.Opacity;
        if (SwipeBubble.RenderTransform is TranslateTransform shift)
        {
            shift.X = back ? plan.Offset : -plan.Offset;
        }

        SwipeLayer.IsVisible = true;
    }

    // What has the keyboard, as far as Alt+← / Alt+→ go: a text box keeps those keys (the Tasks column header moves its column with Ctrl+Alt+← / →).
    private NavigationFocus FocusForNavigation()
    {
        if (FocusManager?.GetFocusedElement() is not Visual focused)
        {
            return NavigationFocus.Other;
        }

        if (focused is TextBox || focused.FindAncestorOfType<TextBox>() is not null)
        {
            return NavigationFocus.TextInput;
        }

        return NavigationFocus.Other;
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
            // Tasks redesign 7.06, 9.10: on the Tasks page Ctrl+F puts the caret in the page's own search; elsewhere the title-bar menu opens
            // with the caret in its search.
            // With the Archive sheet open it is the Archive's search; with the editor popup or Move and label open, nothing.
            if (viewModel.IsTasksSelected && ShownTasksView() is { } tasksView)
            {
                if (tasksView.TakesFindKey)
                {
                    CloseTitleMenu();
                    tasksView.FocusSearch();
                }
            }
            else
            {
                OpenTitleMenu();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            // The title bar's "Start session": the app's start controls, only with no session running here (while another device's
            // session is live they open too, and say so: Start is greyed there).
            if (viewModel.SessionControl is { } control && (control.IsIdle || control.IsEnded || control.IsElsewhere))
            {
                control.StartCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.R && e.KeyModifiers == KeyModifiers.Control)
        {
            // The same as the title-bar menu's Sync now, from anywhere (9.18).
            if (viewModel.RefreshNowCommand.CanExecute(null))
            {
                viewModel.RefreshNowCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.OemPipe or Key.OemBackslash && viewModel.IsTasksSelected && viewModel.Tasks is { } tasks)
        {
            // Ctrl+\ on the Tasks page: show the Shelf hidden during a session, or hide it again (round 2 R3; nothing while planning).
            tasks.ToggleShelfCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.OemPipe or Key.OemBackslash && viewModel.IsTodaySelected
                 && viewModel.Session.Sidebar is { } sidebar)
        {
            // Ctrl+\ on Today (round 2): slide the Today sidebar over the list, or hide it to its rail.
            sidebar.ToggleCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.J && e.KeyModifiers == KeyModifiers.Control && viewModel.Tasks is { } jotPage)
        {
            // Ctrl+J: Jot it down, on the Today page (round 2 R3: the Tasks page has no Today column, so no Jot of its own).
            viewModel.SelectedTab = MainWindowViewModel.TodayTab;

            jotPage.OpenJot();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && viewModel.IsSearchOpen)
        {
            // Esc closes the title-bar menu (in its search, Esc first clears a typed search: TitleMenuView).
            CloseTitleMenu();
            e.Handled = true;
        }
        else if (NavigationKeys.FromKey(e.Key, e.KeyModifiers, e.Handled, FocusForNavigation()) is HistoryDirection direction)
        {
            // Alt+← / Alt+→ (owner 2026-11-14): back / forward, unless what has the keyboard uses them.
            viewModel.Go(direction);
            e.Handled = true;
        }
        else if (_taskShortcuts.Handle(e, viewModel, FocusManager?.GetFocusedElement(), PanelOverPage(viewModel)))
        {
            e.Handled = true;
        }
    }

    // The Tasks page while it is on screen, else null.
    private TasksView? ShownTasksView()
    {
        return Pages.Children[(int)MainTab.Tasks] is TasksView { IsEffectivelyVisible: true } tasksView ? tasksView : null;
    }

    // A panel of the window's own (not in the overlay) is over the page: the title-bar menu, or the Tasks page's right-click menu. The page's
    // keys (Del, F2, H, Ctrl+E, Ctrl+U, Ctrl+G, Esc's steps) must not act on the selection behind it (TaskShortcuts).
    private bool PanelOverPage(MainWindowViewModel viewModel)
    {
        return viewModel.IsSearchOpen || (viewModel.IsTasksSelected && ShownTasksView() is { IsContextMenuOpen: true });
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
        // Tasks redesign 9.09: the labels this device shows and their categories, from the Tasks page's label row.
        search.LabelEntries = () => viewModel.Tasks is { } tasks ? LabelSearchSource.Entries(tasks.Shelf.MenuGroups) : [];
        // Settings' own Search field finds rows by the same words.
        viewModel.Settings.RowsMatching = query => SearchPages()
            .Where(entry => entry.Target is { Tab: SearchTabs.Settings, SettingsGroup: not null } && TextMatcher.Find(entry.Text, query).Count > 0)
            .Select(entry => entry.Target.SettingsGroup!.Value)
            .ToHashSet();
        // Highlights show only while the search is open (owner, AB-Improvements #25): closing it by clicking away keeps the typed
        // words for next time, but the page shows no highlights until it opens again.
        void ShowHighlights() => Highlights.Query = viewModel.IsSearchOpen ? search.Query : null;
        search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.Query))
            {
                ShowHighlights();
            }
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsSearchOpen))
            {
                ShowHighlights();
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
            section => viewModel.Settings.Select(section),
            template => viewModel.Plan?.OpenDayTypes(template),
            control => Dispatcher.UIThread.Post(() => RevealFound(control as Control), DispatcherPriority.Background),
            labels => viewModel.Tasks?.Shelf.ShowOnlyLabels(labels));
        search.OpenRequested += (_, target) =>
        {
            navigator.Open(target);
            // A label or category: the Tasks page is now filtered to it, so the menu gets out of the way. Other results keep it open, so the
            // page's highlights and ▲ ▼ stay at hand (as the old search box did).
            if (target.Labels is not null)
            {
                CloseTitleMenu();
            }

            // Words inside the Plan page's sheets: open the sheet that holds them, then the reveal below finds them on screen.
            if (target.Control is Control found && Pages.Children[(int)MainTab.Plan] is PlanView plan)
            {
                plan.OpenSheetContaining(found);
            }

            // Words inside Stats' Day details: open the fold that holds them.
            if (Pages.Children[(int)MainTab.Stats] is StatsView stats)
            {
                if (target.Control is Control inDetails)
                {
                    stats.OpenFoldHolding(inDetails);
                }
                else if (target.Tab == SearchTabs.Stats)
                {
                    stats.OpenDetails();
                }
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

        // A rail click or search result during a back / forward slide: the slide ends at once (its place is applied), then the click goes on.
        if (!_historyMoving && _historySlide is { IsDone: false } slide)
        {
            slide.FinishNow();
            if (viewModel.SelectedTab != selected)
            {
                viewModel.SelectedTab = selected;
                return;
            }

            if (selected == _shownTab)
            {
                return;
            }
        }

        // Settings with unsaved changes: stay there while it asks Save / Discard / Stay.
        if (!viewModel.MayLeaveTab(_shownTab, selected))
        {
            int back = _shownTab;
            Dispatcher.UIThread.Post(() => viewModel.SelectedTab = back);
            return;
        }

        _shownTab = selected;
        // Back / forward: the tab really shown is where the user went (a back / forward move isn't recorded again).
        viewModel.RecordPlace();

        for (int i = 0; i < Pages.Children.Count; i++)
        {
            Pages.Children[i].IsVisible = i == selected;
        }

        // The page coming into view fades and lifts in (Settings → Animations); a back / forward move slides it in itself.
        if (!_historyMoving)
        {
            AccountabiliBuddy.App.Services.Motion.Current?.Enter(Pages.Children[_shownTab]);
        }

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
        // Windows shutting down is not a click on the X: cancelling it (or asking about unsaved changes) would hold the shutdown up.
        if (!AllowClose && e.CloseReason != WindowCloseReason.OSShutdown)
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
