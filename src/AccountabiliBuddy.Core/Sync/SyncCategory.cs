namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One of a device's task categories as its sync file lists them, so the other devices can offer them when adding a task: the name
/// and the colour only (a category's id is local to a device). The name is the user's own text: sealed in the files, never logged.
/// </summary>
/// <param name="Name">The category's name.</param>
/// <param name="Color">"#RRGGBB".</param>
public sealed record SyncCategory(string Name, string Color);
