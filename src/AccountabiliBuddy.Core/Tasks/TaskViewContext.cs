using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What a view needs besides its settings to pick and order tasks (<see cref="TaskViewRules"/>).</summary>
/// <param name="Now">The trusted time: "untouched" and overdue count from it.</param>
/// <param name="Zone">The zone that says which day it is (due dates are local dates).</param>
/// <param name="Labels">The labels, for grouping by label (their order and names).</param>
/// <param name="Search">Search words (a number like "142b", or text); only matching tasks stay. Null or blank for none.</param>
/// <param name="Scope">The bins the view is about (the Shelf's three for the Shelf); null for every bin. Tasks elsewhere never show.</param>
/// <param name="ShowEmptyBins">Group by bins lists every bin of the scope, empty ones too.</param>
public sealed record TaskViewContext(
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    IReadOnlyList<TaskLabel> Labels,
    string? Search = null,
    IReadOnlyList<TaskBin>? Scope = null,
    bool ShowEmptyBins = false);
