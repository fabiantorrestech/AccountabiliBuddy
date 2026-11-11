using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A saved view of the task list (ADR 76), shared between the user's devices. Replaces the PC's device-local saved filters
/// ("tasks.filterPresets"); their search words stay on the PC (never synced). Three built-in views have fixed ids.
/// </summary>
/// <param name="Name">The user's name for it (private text, sealed).</param>
/// <param name="Icon">A Material icon name; null for the default bookmark.</param>
/// <param name="Config">What it shows.</param>
/// <param name="Position">Its place among the views.</param>
/// <param name="EditedAt">Its newest change; the newest version wins.</param>
/// <param name="BuiltIn">One of the three built-in views (Default, Quick wins, Deep work).</param>
/// <param name="DeletedAt">Set on a tombstone.</param>
public sealed record TaskView(
    Guid Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Icon,
    [property: JsonPropertyName("cfg")] TaskViewConfig Config,
    double Position,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool BuiltIn = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    public static readonly Guid DefaultId = new("0e4f6a52-1b7d-4c39-9f0e-7a2d5c8b1e01");
    public static readonly Guid QuickWinsId = new("0e4f6a52-1b7d-4c39-9f0e-7a2d5c8b1e02");
    public static readonly Guid DeepWorkId = new("0e4f6a52-1b7d-4c39-9f0e-7a2d5c8b1e03");

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>The three built-in views, stamped <paramref name="editedAt"/>: Default (everything), Quick wins (25 min or less), Deep work (50 min or more).</summary>
    public static IReadOnlyList<TaskView> BuiltIns(DateTimeOffset editedAt, TaskViewConfig? defaultConfig = null)
    {
        return
        [
            new TaskView(DefaultId, "Default", "star", defaultConfig ?? TaskViewConfig.Everything, 1, editedAt, BuiltIn: true),
            new TaskView(QuickWinsId, "Quick wins", "lightning-bolt", new TaskViewConfig(new TaskViewFilter(Time: ["le25"])), 2, editedAt, BuiltIn: true),
            new TaskView(DeepWorkId, "Deep work", "head-cog", new TaskViewConfig(new TaskViewFilter(Time: ["ge50"])), 3, editedAt, BuiltIn: true),
        ];
    }
}
