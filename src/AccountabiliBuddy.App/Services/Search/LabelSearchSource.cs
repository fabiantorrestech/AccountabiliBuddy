using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// The task labels and label categories as search entries (Tasks redesign 9.09), read from the Tasks page's label row (the labels this
/// device shows, in category order). A label result opens Tasks filtered to it; a category result, filtered to all its labels.
/// </summary>
public static class LabelSearchSource
{
    public static IReadOnlyList<SearchEntry> Entries(IEnumerable<LabelFilterGroupViewModel> groups)
    {
        var target = new SearchTarget(SearchTabs.Tasks);
        var labels = new List<SearchEntry>();
        var categories = new List<SearchEntry>();
        foreach (LabelFilterGroupViewModel group in groups)
        {
            List<Guid> ids = group.Items.Select(item => item.Id).ToList();
            if (ids.Count == 0)
            {
                continue;
            }

            // Labels with no category ("Other") are listed, but "Other" isn't a category of the user's.
            if (group.GroupId is not null)
            {
                string count = ids.Count == 1 ? "1 label" : $"{ids.Count} labels";
                categories.Add(new SearchEntry(group.Name, $"Labels › Categories · {count}", SearchKinds.Category, target with { Labels = ids }));
            }

            foreach (LabelFilterChipViewModel label in group.Items)
            {
                labels.Add(new SearchEntry(label.Name, $"Labels › {group.Name}", SearchKinds.Label, target with { Labels = [label.Id] }));
            }
        }

        return [.. categories, .. labels];
    }
}
