using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Fills a ⋯ menu from the row it belongs to each time it opens (see <see cref="ITaskMenuSource"/>): a row costs nothing until its
/// menu is used, and the entries always match what the row is now (the label it has, the moves that make sense). Every menu shown
/// here follows the shared popover rules (<see cref="TaskMenuFlyout"/>, <see cref="TaskPopover"/>), its submenus too, and the shared
/// menu look (round 2, brief §3.6): groups split by thin dividers, section heads as small muted labels, what an entry is set to now
/// right-aligned in muted text, › for submenus and pages, ✓ for the chosen one, a destructive entry in red, and no shortcut letters.
/// </summary>
public static class TaskMenus
{
    /// <summary>The class a row (or any container) carries so the menu it opens can outline it while open (brief §3.6).</summary>
    public const string MenuHostClass = "abMenuHost";

    /// <summary>Set on the menu host while its menu is open: its <c>Border.abMenuRing</c> shows (Themes/AbStyles.axaml).</summary>
    public const string MenuOpenClass = "abMenuOpen";

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

    /// <summary>
    /// Opens a menu of <paramref name="entries"/> under or beside <paramref name="button"/>; <paramref name="closed"/> runs once it
    /// closes. The nearest container with <see cref="MenuHostClass"/> (the row) gets an accent outline while the menu is open.
    /// </summary>
    /// <param name="fromKeyboard">Opened with the Menu key or Shift+F10: the first entry takes the keyboard at once.</param>
    public static void ShowEntries(
        Control button,
        IEnumerable<TaskMenuEntry> entries,
        PlacementMode placement,
        Action<TaskMenuEntry>? viewAction = null,
        Action? closed = null,
        bool fromKeyboard = false)
    {
        var flyout = new TaskMenuFlyout { Placement = placement, ViewAction = viewAction, FocusFirstItem = fromKeyboard };
        foreach (Control item in Build(entries, viewAction))
        {
            flyout.Items.Add(item);
        }

        Control? host = MenuHostOf(button);
        host?.Classes.Set(MenuOpenClass, true);
        flyout.Closed += (_, _) =>
        {
            host?.Classes.Set(MenuOpenClass, false);
            closed?.Invoke();
        };

        flyout.ShowAt(button);
    }

    // The control itself or its nearest ancestor that asked to be outlined while its menu is open; null for none.
    private static Control? MenuHostOf(Control control)
    {
        for (Visual? visual = control; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control candidate && candidate.Classes.Contains(MenuHostClass))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<Control> Build(IEnumerable<TaskMenuEntry> entries, Action<TaskMenuEntry>? viewAction)
    {
        foreach (TaskMenuEntry entry in entries)
        {
            if (entry.IsSeparator)
            {
                yield return new Separator { Classes = { "abMenuDivider" } };
                continue;
            }

            var item = new MenuItem { Header = HeaderOf(entry), Command = entry.Command, Tag = entry };
            AutomationProperties.SetName(item, AccessibleName(entry));
            if (entry.IsHeading)
            {
                item.Classes.Add("abMenuHeading");
            }

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

    // What a screen reader says: the text, then what it is set to ("Move, Next"), then "(chosen)" for the ticked one.
    private static string AccessibleName(TaskMenuEntry entry)
    {
        string name = entry.Value is { Length: > 0 } value ? $"{entry.Header}, {value}" : entry.Header;
        return entry.IsChecked ? name + " (chosen)" : name;
    }

    // A 16 px icon in Ab.Muted, or Ab.Drift on a danger entry (followed live, so a theme change recolours it).
    private static MaterialIcon MenuIcon(Material.Icons.MaterialIconKind kind, string colorKey)
    {
        var icon = new MaterialIcon { Kind = kind, Width = 16, Height = 16 };
        icon.Bind(TemplatedControl.ForegroundProperty, icon.GetResourceObservable(colorKey));
        return icon;
    }

    // The entry's look: a label's dot and the text (with a muted note under it) on the left; at the right, in muted text, what it is
    // set to now, then ✓ for the chosen one and › for a panel or page. Headings are small muted labels; danger text is red. Plain text
    // when there is nothing else. The keys that run an entry (TaskMenuEntry.Shortcut) are never drawn.
    private static object HeaderOf(TaskMenuEntry entry)
    {
        bool hasRight = entry.Value is { Length: > 0 } || entry.IsChecked || entry.OpensMore;
        if (entry.Dot is null && !hasRight && !entry.Indented && !entry.IsHeading && !entry.IsDanger && entry.Note is null)
        {
            return entry.Header;
        }

        var row = new DockPanel { LastChildFill = true };
        if (hasRight)
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (entry.Value is { Length: > 0 } value)
            {
                right.Children.Add(Text(value, "abMenuValue"));
            }

            if (entry.IsChecked)
            {
                right.Children.Add(Text("✓", "abMenuCheck"));
            }

            if (entry.OpensMore)
            {
                right.Children.Add(Text("›", "abMenuMore"));
            }

            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
        }

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(entry.Indented ? 14 : 0, 0, 0, 0) };
        if (entry.Dot is IBrush dot)
        {
            left.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = dot,
                Stroke = new SolidColorBrush(Color.Parse("#80808080")),
                StrokeThickness = 1,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        string textClass = entry.IsHeading ? "abMenuHead" : entry.IsDanger ? "abMenuDanger" : "abMenuText";
        TextBlock text = Text(entry.Header, textClass);
        if (entry.IsHeading)
        {
            // A task's title as the heading: one line, cut short.
            text.MaxWidth = 280;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
        }

        if (entry.Note is { Length: > 0 } note)
        {
            var lines = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(text);
            TextBlock noteText = Text(note, "abMenuNote");
            noteText.MaxWidth = 260;
            noteText.TextWrapping = TextWrapping.Wrap;
            lines.Children.Add(noteText);
            left.Children.Add(lines);
        }
        else
        {
            left.Children.Add(text);
        }

        row.Children.Add(left);
        return row;
    }

    // A text in one of the shared menu classes (Themes/AbStyles.axaml), so its colour follows the theme live.
    private static TextBlock Text(string text, string styleClass)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.Classes.Add(styleClass);
        return block;
    }
}
