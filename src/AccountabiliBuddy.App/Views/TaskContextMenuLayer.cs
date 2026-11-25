using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Tasks page's right-click menu (Tasks redesign 8a, SPEC "Right-click menu", boards 19 / 20 / 30): a layer over the page (not dimmed)
/// that draws the menu at the pointer and one submenu beside the row that opened it. Frame: 260 wide, padding 6, Surface2, a 1 px Line2
/// border, radius 12, shadow 0 14 40 in Ab.Shadow's colour, fading in 6 px down over 0.12 s. The heading on top, the entries (the Edit
/// group on its guide line, key chips at the right edge, › for a submenu), the footer "Esc closes this menu · Esc again clears the
/// selection". It opens at least 8 from the page's edges and leaves room for its submenu on the right; taller than the room below, it
/// moves up; still too tall, its list scrolls. Keys while it is open: ↑/↓ move, → / Enter / Space open or run, ← back, Esc closes the whole
/// menu (the selection stays), and the entries' own keys (M, L, T open their submenus; D, H, Del, F2 run; Ctrl+E, Ctrl+U, Ctrl+A). Letters
/// are off while the inline "New tag…" box has the keyboard. A click or right-click outside closes it.
/// </summary>
public sealed class TaskContextMenuLayer : Canvas
{
    private const double MenuWidth = 260;
    private const double SubWidth = 230;
    private const double MoveSubWidth = 200;
    private const double SubMaxHeight = 380;
    private const double EdgeGap = 8;

    // The submenu overlaps the menu's right edge by this much, and its top sits this far above the row that opened it.
    private const double SubOverlap = 6;
    private const double SubLift = 7;

    // The menu's left edge stays at most this far from the page's right edge, so the submenu fits beside it (the mockup's "100% - 500px").
    private const double RoomForSubmenu = 500;

    private static readonly TimeSpan OpenTime = TimeSpan.FromMilliseconds(120);

    private readonly List<(Button Button, TaskMenuEntry Entry)> _items = [];
    private readonly List<(Button Button, TaskMenuEntry Entry)> _subItems = [];
    private Border? _menu;
    private Border? _sub;
    private TaskMenuEntry? _subOwner;
    private Button? _subOwnerButton;
    private Action<TaskMenuEntry>? _viewAction;
    private Control? _returnFocus;

    // The page's size when the menu opened.
    private Size _openedRoom;

    public TaskContextMenuLayer()
    {
        Background = Brushes.Transparent;
        IsVisible = false;
        AddHandler(KeyDownEvent, OnMenuKeyDown, RoutingStrategies.Tunnel);
        PointerPressed += OnOutsidePressed;
        // The page changed size under an open menu (the window resized): it would no longer sit where it was opened.
        SizeChanged += (_, change) =>
        {
            if (IsOpen && change.NewSize != _openedRoom)
            {
                Close(restoreFocus: false);
            }
        };
    }

    /// <summary>The menu is showing.</summary>
    public bool IsOpen => _menu is not null;

    /// <summary>Raised once the menu has closed (whatever closed it).</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Opens the menu of <paramref name="entries"/> at <paramref name="at"/> (in this layer's coordinates). The first heading is the menu's
    /// heading; <paramref name="viewAction"/> answers the entries only the view can (Copy ID, Reminders &amp; due date); the keyboard goes back
    /// to <paramref name="returnFocus"/> when the menu closes. <paramref name="fromKeyboard"/>: the first entry takes the keyboard at once.
    /// </summary>
    public void Open(IReadOnlyList<TaskMenuEntry> entries, Point at, Action<TaskMenuEntry>? viewAction, Control? returnFocus, bool fromKeyboard)
    {
        Close(restoreFocus: false);
        _viewAction = viewAction;
        _returnFocus = returnFocus;
        _menu = BuildMenu(entries);
        Children.Add(_menu);
        IsVisible = true;
        PlaceMenu(at);
        FadeIn(_menu);
        Dispatcher.UIThread.Post(() => TakeKeyboard(fromKeyboard), DispatcherPriority.Input);
    }

    /// <summary>Closes the menu and its submenu (the selection stays); the keyboard goes back to the row when <paramref name="restoreFocus"/>.</summary>
    public void Close(bool restoreFocus = true)
    {
        if (_menu is null)
        {
            return;
        }

        CloseSub();
        Children.Clear();
        _items.Clear();
        _menu = null;
        IsVisible = false;
        Control? back = _returnFocus;
        _returnFocus = null;
        if (restoreFocus && back is { IsEffectivelyVisible: true } && TopLevel.GetTopLevel(back) is not null)
        {
            back.Focus(NavigationMethod.Directional);
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Building ----

    private Border BuildMenu(IReadOnlyList<TaskMenuEntry> entries)
    {
        var list = new StackPanel();
        string? heading = null;
        foreach (TaskMenuEntry entry in entries)
        {
            if (heading is null && entry.IsHeading && entry.Icon is null && list.Children.Count == 0)
            {
                heading = entry.Header;
                continue;
            }

            list.Children.Add(BuildEntry(entry, _items, inSub: false, rowHeight: 32, padding: 8, gap: 10));
        }

        var dock = new DockPanel { LastChildFill = true };
        if (heading is not null)
        {
            TextBlock title = Text(heading, 11.5, "Ab.Muted", FontWeight.SemiBold);
            title.Margin = new Thickness(10, 6, 10, 6);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            DockPanel.SetDock(title, Dock.Top);
            dock.Children.Add(title);
        }

        TextBlock footer = Text("Esc closes this menu · Esc again clears the selection", 11, "Ab.Muted2");
        footer.Margin = new Thickness(10, 6, 10, 2);
        footer.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer
        {
            Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        Border frame = Frame(MenuWidth, dock);
        frame.Focusable = true;
        AutomationProperties.SetName(frame, heading ?? "Task menu");
        return frame;
    }

    private Border BuildSub(TaskMenuEntry owner)
    {
        // Move to's rows are 34 high (padding 0 10), Label's 30, the others 32 (boards 19, 20, 30).
        bool move = owner.Id == "move";
        double rowHeight = move ? 34 : owner.Id == "label" ? 30 : 32;
        var list = new StackPanel();
        foreach (TaskMenuEntry entry in owner.Children ?? [])
        {
            list.Children.Add(BuildEntry(entry, _subItems, inSub: true, rowHeight, padding: move ? 10 : 8, gap: move ? 10 : 9));
        }

        var scroller = new ScrollViewer
        {
            Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Border frame = Frame(move ? MoveSubWidth : SubWidth, scroller);
        AutomationProperties.SetName(frame, owner.Header);
        return frame;
    }

    // The shared frame: padding 6, Surface2, a 1 px Line2 border, radius 12, shadow 0 14 40 in Ab.Shadow's colour.
    private static Border Frame(double width, Control child)
    {
        var frame = new Border
        {
            Width = width,
            Padding = new Thickness(6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = child,
        };
        frame.Classes.Add("ctxMenuFrame");
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Ab.Surface2"));
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Ab.Line2"));
        frame.Bind(Border.BoxShadowProperty, frame.GetResourceObservable("Ab.ShadowColor", value => value is Color color
            ? new BoxShadows(new BoxShadow { OffsetY = 14, Blur = 40, Color = color })
            : default(BoxShadows)));
        return frame;
    }

    private Control BuildEntry(TaskMenuEntry entry, List<(Button, TaskMenuEntry)> sink, bool inSub, double rowHeight, double padding, double gap)
    {
        if (entry.IsSeparator)
        {
            var line = new Border { Height = 1, Margin = inSub ? new Thickness(4) : new Thickness(4, 5) };
            line.Bind(Border.BackgroundProperty, line.GetResourceObservable("Ab.Line2"));
            return line;
        }

        if (entry.IsHeading)
        {
            return GroupHeading(entry, inSub);
        }

        Control item = entry.Submit is not null ? InputSlot(entry, sink, rowHeight, padding, gap) : ItemButton(entry, sink, inSub, rowHeight, padding, gap);
        if (!entry.OnGuide)
        {
            return item;
        }

        // The Edit group's items (16 in, 6 more past a 1 px Line2 guide line) and a category's labels (14 in, 8 more), boards 19 / 20.
        var guide = new Border
        {
            Margin = new Thickness(inSub ? 14 : 16, 0, 0, 0),
            Padding = new Thickness(inSub ? 8 : 6, 0, 0, 0),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = item,
        };
        guide.Bind(Border.BorderBrushProperty, guide.GetResourceObservable("Ab.Line2"));
        return guide;
    }

    // "Edit" (a pencil in the accent, 12 bold Ink2) or a category of the Label submenu (its icon in the accent); a scope heading has no icon.
    private static Control GroupHeading(TaskMenuEntry entry, bool inSub)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = inSub ? new Thickness(8, 6, 8, 3) : new Thickness(10, 4, 10, 2) };
        if (entry.Icon is MaterialIconKind kind)
        {
            row.Children.Add(Icon(kind, 14, "Ab.Accent"));
            row.Children.Add(Text(entry.Header, 12, "Ab.Ink2", FontWeight.Bold));
        }
        else
        {
            row.Children.Add(Text(entry.Header, 11.5, "Ab.Muted", FontWeight.SemiBold));
        }

        return row;
    }

    private Button ItemButton(TaskMenuEntry entry, List<(Button, TaskMenuEntry)> sink, bool inSub, double rowHeight, double padding, double gap)
    {
        var content = new DockPanel { LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(gap, 0, 0, 0) };
        string? value = entry.Value ?? (entry.Command is null && entry.Children is null && entry.ViewAction is null ? entry.Note : null);
        if (value is { Length: > 0 })
        {
            right.Children.Add(Text(value, 11, "Ab.Muted"));
        }

        if (entry.Shortcut is { Length: > 0 } key)
        {
            var chip = new Border { Padding = new Thickness(5, 0), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Child = Text(key, 11, "Ab.Muted") };
            chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("Ab.Surface"));
            right.Children.Add(chip);
        }

        if (entry.Children is { Count: > 0 })
        {
            right.Children.Add(Icon(MaterialIconKind.ChevronRight, 13, "Ab.Muted"));
        }

        DockPanel.SetDock(right, Dock.Right);
        content.Children.Add(right);
        if (Lead(entry) is { } lead)
        {
            lead.Margin = new Thickness(0, 0, gap, 0);
            DockPanel.SetDock(lead, Dock.Left);
            content.Children.Add(lead);
        }

        TextBlock text = Text(entry.Header, 13, entry.IsDanger ? "Ab.Drift" : "Ab.Ink", entry.IsStrong ? FontWeight.SemiBold : FontWeight.Normal);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        content.Children.Add(text);

        var button = new Button
        {
            Content = content,
            Height = rowHeight,
            Padding = new Thickness(padding, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusAdorner = null,
            Classes = { "ctxItem" },
            Tag = entry,
        };
        button.Classes.Set("strong", entry.IsStrong);
        AutomationProperties.SetName(button, entry.Value is { Length: > 0 } named ? $"{entry.Header}, {named}" : entry.Header);
        if (entry.Shortcut is { Length: > 0 } accelerator)
        {
            AutomationProperties.SetAcceleratorKey(button, accelerator);
        }

        bool works = entry.Children is { Count: > 0 } || entry.ViewAction is not null || entry.Command is not null || entry.Submit is not null;
        button.IsEnabled = works;
        button.Click += (_, e) =>
        {
            e.Handled = true;
            Activate(entry, button);
        };
        if (!inSub)
        {
            // Hovering a row with a submenu opens it beside the row; hovering any other row closes the open one (8a.52, 8a.53).
            button.PointerEntered += (_, _) =>
            {
                if (entry.Children is { Count: > 0 })
                {
                    OpenSub(entry, button, focusFirst: false);
                }
                else
                {
                    CloseSub();
                }
            };
        }

        sink.Add((button, entry));
        return button;
    }

    // What leads the row: the entry's icon (15; Drift on Delete, the accent on the strong row and AccentIcon ones, else Muted), a label's
    // 8 px dot, or No label's hollow ring.
    private static Control? Lead(TaskMenuEntry entry)
    {
        if (entry.Icon is MaterialIconKind kind)
        {
            string color = entry.IsDanger ? "Ab.Drift" : entry.IsStrong || entry.AccentIcon ? "Ab.Accent" : "Ab.Muted";
            return Icon(kind, 15, color);
        }

        if (entry.Dot is IBrush dot)
        {
            return new Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Center };
        }

        if (entry.Id == "label.none")
        {
            var ring = new Ellipse { Width = 8, Height = 8, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
            ring.Bind(Shape.StrokeProperty, ring.GetResourceObservable("Ab.Muted2"));
            return ring;
        }

        return null;
    }

    // "New tag…" (8a.64, O6): an entry that, chosen, turns into an inline box (a dashed Line3 outline, "+ New tag · Enter adds it to all").
    private Control InputSlot(TaskMenuEntry entry, List<(Button, TaskMenuEntry)> sink, double rowHeight, double padding, double gap)
    {
        var slot = new Panel();
        slot.Children.Add(ItemButton(entry, sink, inSub: true, rowHeight, padding, gap));
        return slot;
    }

    private void BeginInput(Button button, TaskMenuEntry entry)
    {
        if (button.Parent is not Panel slot)
        {
            return;
        }

        var outline = new Rectangle { RadiusX = 8, RadiusY = 8, StrokeThickness = 1, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        outline.Bind(Shape.StrokeProperty, outline.GetResourceObservable("Ab.Line3"));
        var box = new TextBox
        {
            Height = 30,
            PlaceholderText = "+ New tag · Enter adds it to all",
            Classes = { "ctxNewTag" },
            Tag = entry,
        };
        AutomationProperties.SetName(box, "New tag for all of them");
        var holder = new Panel { Margin = new Thickness(0, 2, 0, 0), Height = 30 };
        holder.Children.Add(outline);
        holder.Children.Add(box);
        slot.Children.Clear();
        slot.Children.Add(holder);
        int index = _subItems.FindIndex(pair => ReferenceEquals(pair.Button, button));
        if (index >= 0)
        {
            _subItems.RemoveAt(index);
        }

        Dispatcher.UIThread.Post(() => box.Focus(NavigationMethod.Pointer), DispatcherPriority.Input);
    }

    // ---- Opening submenus and running entries ----

    // A submenu's row opens it (its first row taking the keyboard), "New tag…" turns into its box, anything else runs and closes the menu.
    private void Activate(TaskMenuEntry entry, Button button)
    {
        if (entry.Children is { Count: > 0 })
        {
            OpenSub(entry, button, focusFirst: true);
            return;
        }

        if (entry.Submit is not null)
        {
            BeginInput(button, entry);
            return;
        }

        Run(entry);
    }

    private void Run(TaskMenuEntry entry)
    {
        Close(restoreFocus: true);
        if (entry.ViewAction is not null)
        {
            _viewAction?.Invoke(entry);
            return;
        }

        if (entry.Command is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void OpenSub(TaskMenuEntry owner, Button ownerButton, bool focusFirst)
    {
        if (_menu is null)
        {
            return;
        }

        if (!ReferenceEquals(_subOwner, owner) || _sub is null)
        {
            CloseSub();
            _subOwner = owner;
            _subOwnerButton = ownerButton;
            ownerButton.Classes.Add("subOpen");
            _sub = BuildSub(owner);
            Children.Add(_sub);
            PlaceSub(ownerButton);
            FadeIn(_sub);
        }

        if (focusFirst)
        {
            Dispatcher.UIThread.Post(() => FocusFirst(_subItems), DispatcherPriority.Input);
        }
    }

    private void CloseSub()
    {
        if (_sub is not null)
        {
            Children.Remove(_sub);
        }

        _subOwnerButton?.Classes.Remove("subOpen");
        _sub = null;
        _subOwner = null;
        _subOwnerButton = null;
        _subItems.Clear();
    }

    // ---- Placing ----

    // The page the layer covers (its parent is arranged even while the layer is hidden, so its size is current).
    private Size Room()
    {
        return (Parent as Control)?.Bounds.Size ?? Bounds.Size;
    }

    // At the pointer, at least 8 from the edges, the left edge at most "width − 500" so the submenu fits to its right (8a.27, 8a.73).
    private void PlaceMenu(Point at)
    {
        if (_menu is null)
        {
            return;
        }

        Size room = Room();
        _openedRoom = room;
        _menu.MaxHeight = Math.Max(120, room.Height - 2 * EdgeGap);
        _menu.Measure(new Size(MenuWidth, _menu.MaxHeight));
        double height = _menu.DesiredSize.Height;
        double left = Math.Min(at.X, room.Width - RoomForSubmenu);
        left = Math.Max(EdgeGap, Math.Min(left, room.Width - EdgeGap - MenuWidth));
        double top = Math.Max(EdgeGap, Math.Min(at.Y, room.Height - EdgeGap - height));
        SetLeft(_menu, left);
        SetTop(_menu, top);
    }

    // Beside the row (its top 7 above the row's, 6 over the menu's right edge), inside the page; on the left when the right has no room.
    private void PlaceSub(Button ownerButton)
    {
        if (_sub is null || _menu is null)
        {
            return;
        }

        Size room = Room();
        double width = _sub.Width;
        _sub.MaxHeight = Math.Min(SubMaxHeight, room.Height - 2 * EdgeGap);
        _sub.Measure(new Size(width, _sub.MaxHeight));
        double height = _sub.DesiredSize.Height;
        double menuLeft = GetLeft(_menu);
        Point rowTop = ownerButton.TranslatePoint(default, this) ?? new Point(0, GetTop(_menu));
        double left = menuLeft + MenuWidth - SubOverlap;
        if (left + width > room.Width - EdgeGap)
        {
            left = Math.Max(EdgeGap, menuLeft - width + SubOverlap);
        }

        double top = Math.Max(EdgeGap, Math.Min(rowTop.Y - SubLift, room.Height - EdgeGap - height));
        SetLeft(_sub, left);
        SetTop(_sub, top);
    }

    // 0.12 s: from transparent and 6 px up to its place (at once when Motion is off).
    private static void FadeIn(Border frame)
    {
        TimeSpan time = Motion.Current?.Scale(OpenTime) ?? OpenTime;
        if (time <= TimeSpan.Zero)
        {
            return;
        }

        frame.Opacity = 0;
        frame.RenderTransform = TransformOperations.Parse("translateY(-6px)");
        frame.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = time },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = time, Easing = new CubicEaseOut() },
        ];
        Dispatcher.UIThread.Post(() =>
        {
            frame.Opacity = 1;
            frame.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }, DispatcherPriority.Render);
    }

    // ---- Keys and the pointer ----

    private void TakeKeyboard(bool fromKeyboard)
    {
        if (_menu is null)
        {
            return;
        }

        if (!fromKeyboard || !FocusFirst(_items))
        {
            _menu.Focus(NavigationMethod.Directional);
        }
    }

    private static bool FocusFirst(List<(Button Button, TaskMenuEntry Entry)> items)
    {
        Button? first = items.Select(pair => pair.Button).FirstOrDefault(button => button.IsEnabled);
        return first?.Focus(NavigationMethod.Directional) == true;
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (_menu is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            // The whole menu closes, submenus included; the selection stays (a second Esc clears it, 8a.71).
            Close(restoreFocus: true);
            e.Handled = true;
            return;
        }

        if (e.Source is TextBox { Tag: TaskMenuEntry { Submit: { } submit } } box)
        {
            // The inline "New tag…" box: Enter adds the tag to all; every other key is the box's own (no menu letters, O6).
            if (e.Key == Key.Enter)
            {
                string typed = box.Text ?? string.Empty;
                e.Handled = true;
                Close(restoreFocus: true);
                if (typed.Trim().TrimStart('#').Length > 0)
                {
                    _ = submit(typed);
                }
            }

            return;
        }

        Visual? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        bool inSub = _sub is not null && focused is not null && _sub.IsVisualAncestorOf(focused);
        List<(Button Button, TaskMenuEntry Entry)> items = inSub ? _subItems : _items;
        switch (e.Key)
        {
            case Key.Down when e.KeyModifiers == KeyModifiers.None:
            case Key.Up when e.KeyModifiers == KeyModifiers.None:
                Step(items, focused, e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                return;
            case Key.Right when e.KeyModifiers == KeyModifiers.None && !inSub:
                if (_items.FirstOrDefault(pair => ReferenceEquals(pair.Button, focused)) is { Entry.Children.Count: > 0 } owner)
                {
                    OpenSub(owner.Entry, owner.Button, focusFirst: true);
                }

                e.Handled = true;
                return;
            case Key.Left when e.KeyModifiers == KeyModifiers.None && inSub:
                Button? back = _subOwnerButton;
                CloseSub();
                back?.Focus(NavigationMethod.Directional);
                e.Handled = true;
                return;
            case Key.Tab:
                Step(items, focused, e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
                e.Handled = true;
                return;
        }

        if (GestureOf(e) is not { } gesture)
        {
            return;
        }

        // The menu's own keys (8a.69, 8a.70): its entries' chips; a letter with Ctrl or Alt does nothing (except Ctrl+E, Ctrl+U, Ctrl+A).
        (Button Button, TaskMenuEntry Entry) keyed = _items.FirstOrDefault(pair => pair.Entry.Shortcut == gesture && pair.Button.IsEnabled);
        if (keyed.Button is null)
        {
            // F2 with several selected: the page's row key (Rename one by one) acts once the menu is out of the way.
            if (gesture == "F2")
            {
                Close(restoreFocus: true);
            }

            return;
        }

        e.Handled = true;
        Activate(keyed.Entry, keyed.Button);
    }

    // The key as an entry's chip reads it: "M", "Del", "F2", "Ctrl+E"; null for keys the menu doesn't run.
    private static string? GestureOf(KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None)
        {
            return e.Key switch
            {
                >= Key.A and <= Key.Z => e.Key.ToString(),
                Key.Delete => "Del",
                Key.F2 => "F2",
                _ => null,
            };
        }

        return e.KeyModifiers == KeyModifiers.Control && e.Key is Key.E or Key.U or Key.A ? "Ctrl+" + e.Key : null;
    }

    private static void Step(List<(Button Button, TaskMenuEntry Entry)> items, Visual? focused, int step)
    {
        List<Button> buttons = items.Select(pair => pair.Button).Where(button => button.IsEnabled && button.IsEffectivelyVisible).ToList();
        if (buttons.Count == 0)
        {
            return;
        }

        int at = buttons.FindIndex(button => ReferenceEquals(button, focused));
        int next = at < 0 ? (step > 0 ? 0 : buttons.Count - 1) : (at + step + buttons.Count) % buttons.Count;
        buttons[next].Focus(NavigationMethod.Directional);
        buttons[next].BringIntoView();
    }

    // A click or right-click outside the menu closes it; the page under it is not dimmed and the click does nothing else (8a.28).
    private void OnOutsidePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, this))
        {
            Close(restoreFocus: false);
            e.Handled = true;
        }
    }

    // ---- Small parts ----

    private static TextBlock Text(string text, double size, string colorKey, FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(colorKey));
        return block;
    }

    private static MaterialIcon Icon(MaterialIconKind kind, double size, string colorKey)
    {
        var icon = new MaterialIcon { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(TemplatedControl.ForegroundProperty, icon.GetResourceObservable(colorKey));
        return icon;
    }
}
