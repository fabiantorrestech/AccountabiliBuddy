namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One of a device's task categories as its sync file lists them, so the other devices can offer them when adding a task and take a
/// newer colour for the same name (ADR 62): the name, the colour and when it was chosen (a category's id is local to a device). The name
/// is the user's own text: sealed in the files, never logged.
/// </summary>
/// <param name="Name">The category's name.</param>
/// <param name="Color">"#RRGGBB".</param>
/// <param name="ColorEditedAt">When the user last chose the colour; null: not recoloured since that came in, older than any stamped colour.</param>
public sealed record SyncCategory(string Name, string Color, DateTimeOffset? ColorEditedAt = null);
