namespace AccountabiliBuddy.App.Services;

/// <summary>One line of the overlay's "what it shows, and in what order" list.</summary>
public sealed record OverlayItem(OverlayItemKind Kind, bool Enabled);
