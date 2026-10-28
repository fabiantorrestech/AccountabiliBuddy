using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// The app's own words as search entries: every text a page declares (titles, descriptions, labels, check box and
/// button text), read from the live pages, so it always matches what the app shows. A result's path is the page name
/// plus the <see cref="SearchArea"/> names around the text; on Settings it also says which row to open.
/// </summary>
public static class UiSearchSource
{
    public static IReadOnlyList<SearchEntry> Entries(IReadOnlyList<(int Tab, string TabName, Control Page)> pages)
    {
        var entries = new List<SearchEntry>();
        var seen = new HashSet<(string Text, string Path)>();
        foreach ((int tab, string tabName, Control page) in pages)
        {
            foreach (Control control in page.GetLogicalDescendants().OfType<Control>())
            {
                string? text = TextOf(control);
                if (text is null)
                {
                    continue;
                }

                (string path, int? group) = PlaceOf(control, page, tabName);
                if (!seen.Add((text, path)))
                {
                    continue;
                }

                string kind = tab == SearchTabs.Settings ? SearchKinds.Setting : SearchKinds.App;
                entries.Add(new SearchEntry(text, path, kind, new SearchTarget(tab, SettingsGroup: group, Control: control)));
            }

            // Words in the flyouts of buttons that opt in: not on the page until opened, so read from the flyout's own content.
            foreach (Control button in page.GetLogicalDescendants().OfType<Control>().Where(SearchArea.GetIndexesFlyout))
            {
                (string path, int? group) = PlaceOf(button, page, tabName);
                string kind = tab == SearchTabs.Settings ? SearchKinds.Setting : SearchKinds.App;
                foreach (FlyoutBase flyout in FlyoutsOf(button))
                {
                    foreach (string text in FlyoutTexts(flyout))
                    {
                        if (seen.Add((text, path)))
                        {
                            entries.Add(new SearchEntry(text, path, kind, new SearchTarget(tab, SettingsGroup: group, Control: button, Flyout: flyout)));
                        }
                    }
                }
            }
        }

        return entries;
    }

    /// <summary>The flyouts a button opens: its own and its attached one.</summary>
    private static IEnumerable<FlyoutBase> FlyoutsOf(Control button)
    {
        if (button is Button { Flyout: { } own })
        {
            yield return own;
        }

        if (FlyoutBase.GetAttachedFlyout(button) is { } attached)
        {
            yield return attached;
        }
    }

    /// <summary>The words in a flyout: a menu's item names, or the texts of its content (as a page's are read).</summary>
    private static IEnumerable<string> FlyoutTexts(FlyoutBase flyout)
    {
        if (flyout is MenuFlyout menu)
        {
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                if (item.Header is string header && !string.IsNullOrWhiteSpace(header))
                {
                    yield return header.Trim();
                }
            }

            yield break;
        }

        if (flyout is Flyout { Content: Control content })
        {
            foreach (Control control in new[] { content }.Concat(content.GetLogicalDescendants().OfType<Control>()))
            {
                if (TextOf(control) is string text)
                {
                    yield return text;
                }
            }
        }
    }

    /// <summary>The words a control shows by itself; null for none, input boxes and plain numbers.</summary>
    private static string? TextOf(Control control)
    {
        string? text = control switch
        {
            TextBox => null,
            TextBlock textBlock => textBlock.Text,
            ContentControl { Content: string content } => content,
            HeaderedContentControl { Header: string header } => header,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        return double.TryParse(trimmed, NumberStyles.Any, CultureInfo.CurrentCulture, out _) ? null : trimmed;
    }

    /// <summary>"Page › Area › Area" from the outside in, and the Settings row the control sits in, if any.</summary>
    private static (string Path, int? Group) PlaceOf(Control control, Control page, string tabName)
    {
        var names = new List<string>();
        int? group = null;
        for (ILogical? current = control; current is not null && !ReferenceEquals(current, page); current = current.LogicalParent)
        {
            if (current is not Control area)
            {
                continue;
            }

            if (SearchArea.GetName(area) is string name && !ReferenceEquals(area, control))
            {
                names.Insert(0, name);
            }

            if (group is null && SearchArea.GetSettingsGroup(area) >= 0)
            {
                group = SearchArea.GetSettingsGroup(area);
            }
        }

        names.Insert(0, tabName);
        return (string.Join(" › ", names), group);
    }
}
