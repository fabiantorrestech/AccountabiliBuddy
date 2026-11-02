using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The overlay tile's body (ADR 51, boards 13a, 14a, 15b): the rounded border, the parts in the order the settings give (ring or timeline, minutes,
/// task name, controls, clock, batteries, sound; each row of the list is its own part) and the outline for where it sits. The live overlay window
/// and the Settings preview both host it; it reads everything from the <see cref="TaskbarOverlayViewModel"/> it is given as its DataContext and
/// knows nothing about windows. When the parts are wider than the room, <see cref="PriorityRowPanel"/> trims the task name and then drops whole parts.
/// </summary>
public partial class OverlayTileView : UserControl
{
    public OverlayTileView()
    {
        InitializeComponent();
    }

    /// <summary>Lays the blocks out for this target and settings, then shapes the outline. Call again whenever either changed.</summary>
    public void Apply(OverlayTarget target, OverlaySettings settings, double heightDips)
    {
        Rebuild(target, settings);
        ApplyShape(target, settings, heightDips);
    }

    private void Rebuild(OverlayTarget target, OverlaySettings settings)
    {
        MainRow.Children.Clear();
        ExtraRow.Children.Clear();
        bool vertical = target.Kind == OverlayTargetKind.VerticalTile;
        MainRow.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        MainRow.Spacing = vertical ? 2 : 12;
        MainRow.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        ExtraRow.IsVisible = false;
        List<OverlayItem> items = settings.Items.Where(item => item.Enabled).ToList();

        if (vertical)
        {
            BuildVertical(items);
            return;
        }

        bool small = settings.Size == OverlaySize.Small;
        bool large = settings.Size == OverlaySize.Large;
        bool wide = settings.Size == OverlaySize.Wide;
        // Time & date and Batteries draw at Medium and up; Small stays one line (over the tray it still brings its own clock).
        bool ownClock = !small || target.CoversTray;
        bool timelineMode = settings.Progress == OverlayProgress.Timeline;
        bool clockAdded = false;
        bool ringAdded = false;
        bool textAdded = false;
        bool stackTimeline = false;
        bool line2Placed = false;

        for (int i = 0; i < items.Count; i++)
        {
            switch (items[i].Kind)
            {
                case OverlayItemKind.Ring when !timelineMode:
                    MainRow.Children.Add(Block(small ? "ringSmall" : "ring"));
                    ringAdded = true;
                    break;
                case OverlayItemKind.Ring:
                    // The timeline right before the text belongs to it (under it, or after it on Small), as the ring sits beside it. Anywhere else
                    // it stands on its own; at Large, which has a second row, it takes that row's whole line.
                    if (i + 1 < items.Count && items[i + 1].Kind is OverlayItemKind.Minutes or OverlayItemKind.Task)
                    {
                        stackTimeline = true;
                    }
                    else if (large)
                    {
                        ExtraRow.Children.Add(Block("timelineLarge"));
                    }
                    else
                    {
                        MainRow.Children.Add(Block(small ? "timelineSmall" : "timeline"));
                    }

                    break;
                case OverlayItemKind.Minutes or OverlayItemKind.Task:
                    // Minutes and the task name that sit next to each other in the list share one block; apart, each stands alone. The second
                    // line goes under the first text block (not on Small, which is one line).
                    var run = new List<OverlayItemKind> { items[i].Kind };
                    while (i + 1 < items.Count && items[i + 1].Kind is OverlayItemKind.Minutes or OverlayItemKind.Task)
                    {
                        run.Add(items[++i].Kind);
                    }

                    MainRow.Children.Add(BuildText(run, withLine2: !small && !line2Placed, withCue: !textAdded, timeline: stackTimeline ? (small ? "timelineSmall" : "timelineStacked") : null, small));
                    line2Placed = true;
                    textAdded = true;
                    stackTimeline = false;
                    break;
                case OverlayItemKind.Controls:
                    MainRow.Children.Add(Block("controls"));
                    break;
                case OverlayItemKind.TimeDate when ownClock:
                    MainRow.Children.Add(Block("clock", target.CoversTray ? 0 : DropClock));
                    clockAdded = true;
                    break;
                case OverlayItemKind.Batteries when ownClock:
                    MainRow.Children.Add(Block("batteries", DropBatteries));
                    break;
                case OverlayItemKind.Sound when large:
                    ExtraRow.Children.Add(Block("sound"));
                    break;
                case OverlayItemKind.Sound when wide:
                    MainRow.Children.Add(Block("sound", DropSound));
                    break;
            }
        }

        if (!ringAdded && !textAdded && MainRow.Children.Count > 0)
        {
            // Drift turns the ring into a dot; with no ring and no text to carry one, the dot stands alone.
            MainRow.Children.Insert(0, Block("dot"));
        }

        if (target.CoversTray)
        {
            // It covers the tray, so it brings the clock and a ⌃ for the real overflow.
            if (!clockAdded)
            {
                MainRow.Children.Add(Block("clock"));
            }

            MainRow.Children.Add(Block("tray"));
        }

        if (MainRow.Children.Count == 0 && ExtraRow.Children.Count == 0)
        {
            // Every part is switched off (or none applies at this size): a small dot in the state colour keeps the tile there to find and click.
            MainRow.Children.Add(Block("marker"));
        }

        ExtraRow.IsVisible = ExtraRow.Children.Count > 0;
    }

    // The side tile (8d) is narrow: the ring, the minutes and Pause follow the list's order; the other parts have no room there.
    private void BuildVertical(List<OverlayItem> items)
    {
        foreach (OverlayItem item in items)
        {
            switch (item.Kind)
            {
                case OverlayItemKind.Ring:
                    MainRow.Children.Add(Block("vRing"));
                    break;
                case OverlayItemKind.Minutes:
                    MainRow.Children.Add(Block("vMinutes"));
                    break;
                case OverlayItemKind.Controls:
                    MainRow.Children.Add(Block("vPause"));
                    break;
            }
        }

        if (MainRow.Children.Count == 0)
        {
            MainRow.Children.Add(Block("marker"));
        }
    }

    /// <summary>
    /// The minutes and/or the task name as one block (<paramref name="parts"/> in list order): they sit on one line, with the second line under
    /// them when this block carries it and a timeline under that when the timeline is stacked here. A drift dot leads when there is no ring to
    /// become one. The task name (and the second line) are what trims first when the row is too wide.
    /// </summary>
    private Control BuildText(IReadOnlyList<OverlayItemKind> parts, bool withLine2, bool withCue, string? timeline, bool small)
    {
        var top = new Grid();
        int column = 0;
        void Place(Control control, GridLength width)
        {
            top.ColumnDefinitions.Add(new ColumnDefinition(width));
            Grid.SetColumn(control, column++);
            top.Children.Add(control);
        }

        if (withCue)
        {
            Place(Block("cue"), GridLength.Auto);
        }

        for (int i = 0; i < parts.Count; i++)
        {
            bool isTask = parts[i] == OverlayItemKind.Task;
            ContentControl part = Block(isTask ? "task" : "minutes");
            part.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
            Place(part, isTask ? new GridLength(1, GridUnitType.Star) : GridLength.Auto);
        }

        if (small && timeline is not null)
        {
            Place(Block(timeline), GridLength.Auto);
        }

        var stack = new StackPanel { Spacing = 1, MinWidth = 40, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(top);
        if (withLine2)
        {
            stack.Children.Add(Block("line2"));
        }

        if (!small && timeline is not null)
        {
            stack.Children.Add(Block(timeline));
        }

        bool shrinks = withLine2 || parts.Contains(OverlayItemKind.Task);
        PriorityRowPanel.SetShrinks(stack, shrinks);
        return stack;
    }

    // Drop order when the row is too wide for its room (see PriorityRowPanel): sound first, then batteries, then the clock.
    private const int DropSound = 1;
    private const int DropBatteries = 2;
    private const int DropClock = 3;

    private ContentControl Block(string key, int dropOrder = 0)
    {
        var block = new ContentControl { Content = DataContext, ContentTemplate = (IDataTemplate)Resources[key]!, VerticalAlignment = VerticalAlignment.Center };
        PriorityRowPanel.SetDropOrder(block, dropOrder);
        return block;
    }
    /// <summary>Docked: the corner next to the app icons is rounded, with a line on the top and that side. Floating and side tiles: rounded all round, or a pill.</summary>
    private void ApplyShape(OverlayTarget target, OverlaySettings settings, double heightDips)
    {
        Tile.Padding = new Thickness(10, 0);
        bool smallDocked = target.Kind == OverlayTargetKind.Docked && settings.Size == OverlaySize.Small;
        if (target.Kind == OverlayTargetKind.Docked && !smallDocked)
        {
            Tile.CornerRadius = target.AlignRight ? new CornerRadius(10, 0, 0, 0) : new CornerRadius(0, 10, 0, 0);
            Tile.BorderThickness = target.AlignRight ? new Thickness(1, 1, 0, 0) : new Thickness(0, 1, 1, 0);
            return;
        }

        Tile.BorderThickness = new Thickness(1);
        Tile.CornerRadius = settings.Shape == OverlayShape.Pill || target.Kind == OverlayTargetKind.VerticalTile
            ? new CornerRadius(Math.Min(heightDips, 999) / 2 > 40 ? 30 : heightDips / 2)
            : new CornerRadius(smallDocked ? 8 : 10);
        if (target.Kind == OverlayTargetKind.VerticalTile)
        {
            Tile.Padding = new Thickness(6, 8);
        }
    }
}