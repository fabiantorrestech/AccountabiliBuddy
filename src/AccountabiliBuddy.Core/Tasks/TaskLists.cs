using System.Text.Json;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Everything the one shared task list (ADR 76) holds on a device, in its sync form: the tasks (tombstones and local stubs
/// included), the labels, the saved views and History. The PC's task store keeps one of these; <see cref="TaskListRules"/> changes it.
/// The text in it is the user's private text: sealed wherever it is stored or synced, never logged.
/// </summary>
/// <param name="Account">
/// The account's Tasks settings (default view, picked calendar, the devices task reminders ring on), synced with the lists; null when nothing was ever chosen. Carried
/// over by every rule that rebuilds the lists.
/// </param>
/// <param name="TagScopes">
/// ADR 89: the tags that are device-only (each with the devices that show it, tombstones included), merged item by item; null when none was
/// ever made. A tag with no live scope is shared. Carried over by every rule that rebuilds the lists; a file without the part leaves them alone.
/// </param>
public sealed record TaskLists(
    IReadOnlyList<SyncTaskItem> Tasks,
    IReadOnlyList<SyncTaskLabel> Labels,
    IReadOnlyList<SyncTaskView> Views,
    IReadOnlyList<SyncTaskLogItem> Log,
    SyncTaskAccountSettings? Account = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTagScope>? TagScopes = null)
{
    public static TaskLists Empty { get; } = new([], [], [], []);

    /// <summary>The lists a migration made.</summary>
    public static TaskLists From(TaskMigrationResult result)
    {
        return new TaskLists(result.Tasks, result.Labels, result.Views, result.History);
    }

    /// <summary>The whole content as one text, to tell whether a change changed anything (never logged: it holds the text).</summary>
    public string Signature()
    {
        return JsonSerializer.Serialize(this, SyncJson.Options);
    }
}
