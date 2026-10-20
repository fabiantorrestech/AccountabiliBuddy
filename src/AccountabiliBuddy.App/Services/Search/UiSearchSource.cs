using System.Globalization;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// The app's own words as search entries: every text a page declares (titles, descriptions, labels, check box and
/// button text), read from the live pages, so it always matches what the app shows. A result's path is the tab name
/// plus the <see cref="SearchArea"/> names around the text; on Settings it also says which group to open.
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
        }

        return entries;
    }

    /// <summary>The words a control shows by itself; null for none, input boxes and plain numbers.</summary>
    private static string? TextOf(Control control)
    {
        string? text = control switch
        {
            TextBox => null,
            TextBlock textBlock => textBlock.Text,
            ContentControl { Content: string content } => content,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        return double.TryParse(trimmed, NumberStyles.Any, CultureInfo.CurrentCulture, out _) ? null : trimmed;
    }

    /// <summary>"Tab › Area › Area" from the outside in, and the Settings group the control sits in, if any.</summary>
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
