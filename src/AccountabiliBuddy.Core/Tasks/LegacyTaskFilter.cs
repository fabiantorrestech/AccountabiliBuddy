namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The PC's old Tasks page filter, as the migration takes it (ADR 76): a saved filter (<c>tasks.filterPresets</c>) or the current
/// one (<c>tasks.view</c>, no name). The App maps its own types into this; the values are the old enum names.
/// </summary>
/// <param name="Id">The preset's id; any id for the current filter.</param>
/// <param name="Name">The preset's name; empty for the current filter.</param>
/// <param name="Statuses">Old status names shown (ComeBack, Later, Unfinished, AutoSaved, Done); empty for all.</param>
/// <param name="CategoryIds">Old category ids shown; empty (with <paramref name="NoCategory"/> off) for all.</param>
/// <param name="NoCategory">Rows without a category are shown too.</param>
/// <param name="Due">Old due filter name (Any, Overdue, Today, ThisWeek, NoDate).</param>
/// <param name="Sort">Old sort name (Default, DueAscending, DueDescending, MostRecent, Oldest).</param>
/// <param name="Search">The search words: private, kept on this PC only, never in a synced view.</param>
public sealed record LegacyTaskFilter(
    Guid Id,
    string Name,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<Guid> CategoryIds,
    bool NoCategory,
    string Due,
    string Sort,
    string Search);
