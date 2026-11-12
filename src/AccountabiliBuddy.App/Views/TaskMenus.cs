using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons.Avalonia;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Fills a ⋯ menu from the row it belongs to each time it opens (see <see cref="ITaskMenuSource"/>): a row costs nothing until its
/// menu is used, and the entries always match what the row is now (the label it has, the moves that make sense). Every menu shown
/// here follows the shared popover rules (<see cref="TaskMenuFlyout"/>, <see cref="TaskPopover"/>), its submenus too.
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

        ShowEntries(button, source.BuildMenu(), PlacementMode.BottomEdgeAlignedRight, viewAction);
    }

    /// <summary>
    /// Opens the category picker of the row a chip or the "+ category" spot belongs to: the same choices as ⋯ › Label (the row is
    /// the button's data context), asked for now so the one the row has is marked.
    /// </summary>
    public static void ShowCategoryPicker(Button button)
    {
        if (button.DataContext is TaskListRowViewModel { CanLabel: true } row)
        {
            ShowEntries(button, row.BuildLabelMenu(), PlacementMode.BottomEdgeAlignedLeft);
        }
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard of <paramref name="visual"/>'s window (Copy ID: "142 · title", the user's own
    /// text, copied only where they asked) and says so in the Tasks toast ("Copied 142 · …"). Another app holding the clipboard means
    /// nothing is copied (false).
    /// </summary>
    public static async Task<bool> CopyAsync(Visual visual, string text)
    {
        if (TopLevel.GetTopLevel(visual) is not { Clipboard: { } clipboard } window)
        {
            return false;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Another app holds the clipboard: nothing is copied.
            return false;
        }

        if (window.DataContext is MainWindowViewModel { Tasks: { } tasks })
        {
            tasks.Toasts.Say(AccountabiliBuddy.Core.Tasks.TaskToasts.Copied(text));
        }

        return true;
    }

    /// <summary>Opens a menu of <paramref name="entries"/> under or beside <paramref name="button"/>; <paramref name="closed"/> runs once it closes.</summary>
    public static void ShowEntries(Control button, IEnumerable<TaskMenuEntry> entries, PlacementMode placement, Action<TaskMenuEntry>? viewAction = null, Action? closed = null)
    {
        var flyout = new TaskMenuFlyout { Placement = placement };
        foreach (Control item in Build(entries, viewAction))
        {
            flyout.Items.Add(item);
        }

        if (closed is not null)
        {
            flyout.Closed += (_, _) => closed();
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
                item.Icon = MenuIcon(kind, entry.IsDanger ? "Ab.Drift" : "Ab.Muted");
            }

            if (entry.Children is { Count: > 0 } children)
            {
                TaskPopover.FollowRules(item);
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

    // A 16 px icon in Ab.Muted, or Ab.Drift on a danger entry (followed live, so a theme change recolours it).
    private static MaterialIcon MenuIcon(Material.Icons.MaterialIconKind kind, string colorKey)
    {
        var icon = new MaterialIcon { Kind = kind, Width = 16, Height = 16 };
        icon.Bind(TemplatedControl.ForegroundProperty, icon.GetResourceObservable(colorKey));
        return icon;
    }

    // A label's dot before its text, a ✓ after the one the task has, a muted hint at the right, red for danger; plain text otherwise.
    private static object HeaderOf(TaskMenuEntry entry)
    {
        if (entry.Dot is null && !entry.IsChecked && !entry.Indented && !entry.IsHeading && !entry.IsDanger && entry.Hint is null)
        {
            return entry.Header;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(entry.Indented ? 14 : 0, 0, 0, 0) };
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

        var text = new TextBlock
        {
            Text = entry.Header,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = entry.IsHeading ? FontWeight.SemiBold : FontWeight.Normal,
        };
        if (entry.IsHeading)
        {
            // A heading is a disabled item, which the theme dims too far on Paper; the app's muted text colour (followed live) stays readable.
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Ab.Muted"));
        }
        else if (entry.IsDanger)
        {
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Ab.Drift"));
        }

        panel.Children.Add(text);
        if (entry.IsChecked)
        {
            panel.Children.Add(new TextBlock { Text = "✓", VerticalAlignment = VerticalAlignment.Center });
        }

        if (entry.Hint is not { Length: > 0 } hint)
        {
            return panel;
        }

        // The hint sits at the right edge of the entry, muted and smaller.
        var hintText = new TextBlock { Text = hint, FontSize = 12, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hintText.Bind(TextBlock.ForegroundProperty, hintText.GetResourceObservable("Ab.Muted"));
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(hintText, Dock.Right);
        row.Children.Add(hintText);
        row.Children.Add(panel);
        return row;
    }
}
