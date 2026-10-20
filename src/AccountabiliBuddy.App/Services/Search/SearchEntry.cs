namespace AccountabiliBuddy.App.Services.Search;

/// <summary>One thing search can find: its text, where it lives and what kind of thing it is.</summary>
/// <param name="Path">Where it is, e.g. "Settings › Your day › Sleep".</param>
/// <param name="Kind">A <see cref="SearchKinds"/> tag.</param>
public sealed record SearchEntry(string Text, string Path, string Kind, SearchTarget Target);
