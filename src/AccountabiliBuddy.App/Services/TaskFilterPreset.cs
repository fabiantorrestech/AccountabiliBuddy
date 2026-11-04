namespace AccountabiliBuddy.App.Services;

/// <summary>A saved filter on the Tasks page: the filter button's choices and the search words, under a name the user gave it.</summary>
/// <param name="View">The filter button's choices (status, category, due, sort).</param>
/// <param name="Search">The search words; empty for none. The user's private text: encrypted where it is stored, never synced or logged.</param>
public sealed record TaskFilterPreset(Guid Id, string Name, TaskViewPreferences View, string Search);
