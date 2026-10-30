using AccountabiliBuddy.App.ViewModels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons.Avalonia;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Fills a ⋯ menu from the row it belongs to each time it opens (see <see cref="ITaskMenuSource"/>): a row costs nothing until its
/// menu is used, and the entries always match what the row is now (the category it has, the moves that make sense).
/// </summary>
public static class TaskMenus
{
    /// <summary>
    /// Opens the ⋯ menu of the row a button belongs to (the row is the button's data context): its entries are asked for now, so
    /// they match what the row is now.
    /// </summary>
    /// <param name="viewAction">Answers the entries only the view can (opening a row's own editor); null if the row has none.</param>
    public static void Show(Button button, Action<TaskMenuEntry>? viewAction = null)
    {
        if (button.DataContext is not ITaskMenuSource source)
        {
            return;
        }

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (Control item in Build(source.BuildMenu(), viewAction))
        {
            flyout.Items.Add(item);
        }

        flyout.ShowAt(button);
    }

    private static IEnumerable<Control> Build(IEnumerable<TaskMenuEntry> entries, Action<TaskMenuEntry>? viewAction)
    {
        foreach (TaskMenuEntry entry in entries)
        {
            if (entry.IsSeparator)
            {
                yield return new Separator();
                continue;
            }

            var item = new MenuItem { Header = HeaderOf(entry), Command = entry.Command };
            AutomationProperties.SetName(item, entry.IsChecked ? $"{entry.Header} (chosen)" : entry.Header);
            if (entry.Icon is { } kind)
            {
                item.Icon = MenuIcon(kind);
            }

            if (entry.Children is { Count: > 0 } children)
            {
                foreach (Control child in Build(children, viewAction))
                {
                    item.Items.Add(child);
                }
            }
            else if (entry.ViewAction is not null)
            {
                TaskMenuEntry answered = entry;
                item.Click += (_, _) => viewAction?.Invoke(answered);
            }
            else if (entry.Command is null)
            {
                item.IsEnabled = false;
            }

            yield return item;
        }
    }

    // A 16 px icon in Ab.Muted (followed live, so a theme change recolours it).
    private static MaterialIcon MenuIcon(Material.Icons.MaterialIconKind kind)
    {
        var icon = new MaterialIcon { Kind = kind, Width = 16, Height = 16 };
        icon.Bind(TemplatedControl.ForegroundProperty, icon.GetResourceObservable("Ab.Muted"));
        return icon;
    }

    // A category's dot before its text, a ✓ after the one the task has; plain text otherwise.
    private static object HeaderOf(TaskMenuEntry entry)
    {
        if (entry.Dot is null && !entry.IsChecked)
        {
            return entry.Header;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (entry.Dot is IBrush dot)
        {
            panel.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = dot,
                Stroke = new SolidColorBrush(Color.Parse("#80808080")),
                StrokeThickness = 1,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        panel.Children.Add(new TextBlock { Text = entry.Header, VerticalAlignment = VerticalAlignment.Center });
        if (entry.IsChecked)
        {
            panel.Children.Add(new TextBlock { Text = "✓", VerticalAlignment = VerticalAlignment.Center });
        }

        return panel;
    }
}
