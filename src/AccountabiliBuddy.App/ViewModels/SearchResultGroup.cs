namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The search results on one tab, e.g. everything found under "Day plan".</summary>
public sealed record SearchResultGroup(string Name, IReadOnlyList<SearchResultRow> Rows);
