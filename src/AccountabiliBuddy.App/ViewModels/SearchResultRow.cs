using AccountabiliBuddy.App.Services.Search;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One search result, its text split around the first match so the match can be highlighted.</summary>
/// <param name="Path">Where it is, e.g. "Settings › Your day › Sleep".</param>
/// <param name="Kind">A <see cref="SearchKinds"/> tag, e.g. "your plan".</param>
public sealed record SearchResultRow(string Before, string Match, string After, string Path, string Kind, SearchTarget Target);
