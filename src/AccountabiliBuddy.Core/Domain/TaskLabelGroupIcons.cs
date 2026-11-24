namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// The icon keys a label group can have (ADR 93): neutral words both apps map to their own Material icon (the PC to
/// Material.Icons.Avalonia, the phone to material-icons-extended, ADR 32). An unknown key (a newer app's) is kept as written and shows
/// <see cref="Default"/>.
/// </summary>
public static class TaskLabelGroupIcons
{
    /// <summary>What a new group starts with, and what an unknown key shows.</summary>
    public const string Default = "folder";

    /// <summary>Every key, in the order a picker lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Default, "work", "home", "person", "people", "school", "heart", "code",
        "money", "cart", "fitness", "book", "star", "flag", "lightbulb", "travel",
        "music", "palette", "calendar", "chat", "health", "pets", "food", "build",
    ];

    /// <summary>Whether <paramref name="key"/> is one of <see cref="All"/>.</summary>
    public static bool IsKnown(string? key)
    {
        return key is not null && All.Contains(key, StringComparer.Ordinal);
    }

    /// <summary>The key to store for a chosen one: itself when it is known, else <see cref="Default"/>.</summary>
    public static string Chosen(string? key)
    {
        return IsKnown(key) ? key! : Default;
    }
}
