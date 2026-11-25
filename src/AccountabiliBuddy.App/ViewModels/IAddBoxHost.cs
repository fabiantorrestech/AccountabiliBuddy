using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What an add box needs from the page it sits on (<see cref="TasksViewModel"/>): the labels and tags to suggest, the user's project
/// markers, and the add itself, which goes through the task list's actions (numbers, stamps, undo, sync) and the session's cap.
/// </summary>
public interface IAddBoxHost
{
    /// <summary>The live labels, in their order.</summary>
    IReadOnlyList<TaskLabel> Labels { get; }

    /// <summary>Every tag the live tasks have, with how many tasks have it, most used first.</summary>
    IReadOnlyList<KeyValuePair<string, int>> TagCounts();

    /// <summary>The project markers the user taught (Settings › Tasks › Project markers).</summary>
    IReadOnlyList<ProjectMarkerPattern> CustomMarkers { get; }

    /// <summary>Settings › Tasks › Title help › Suggest shorter titles: the counter and "Move … to notes" show (on by default).</summary>
    bool SuggestShorterTitles { get; }

    /// <summary>"From your lists": the picker of tasks from the other lists; null where there is none.</summary>
    ListPickerViewModel? FromYourLists { get; }

    /// <summary>A marker style seen often enough to offer learning it ("Treat ⟦…⟧ as a project marker?"); null when there is none to ask.</summary>
    ProjectMarkerCandidate? MarkerToOffer();

    Task LearnMarkerAsync(ProjectMarkerPattern marker);

    Task DeclineMarkerAsync(ProjectMarkerPattern marker);

    /// <summary>A label named <paramref name="name"/> (made when there is none): its id; null for a blank name.</summary>
    Task<Guid?> CreateLabelAsync(string name);

    /// <summary>ADR 89: this device is in a sync group, so a label or tag can be made "Only on this device".</summary>
    bool CanScope => false;

    /// <summary>ADR 89: whether the label is shared (on every device); a device-only one shows under "This device".</summary>
    bool IsLabelShared(TaskLabel label) => true;

    /// <summary>ADR 89: whether the tag is shared.</summary>
    bool IsTagShared(string tag) => true;

    /// <summary>ADR 89: a label named <paramref name="name"/> that only this device shows (a device-only one other devices made joins it).</summary>
    Task<Guid?> CreateDeviceLabelAsync(string name) => CreateLabelAsync(name);

    /// <summary>ADR 89: the tag shows on this device only.</summary>
    Task MakeTagDeviceOnlyAsync(string tag) => Task.CompletedTask;

    /// <summary>Adds the task. False when nothing was added (the box keeps the text).</summary>
    Task<bool> AddFromBoxAsync(AddBoxRequest request);

    /// <summary>Today's date on the trusted clock, in the PC's time zone (the jot's "N today").</summary>
    DateOnly Today { get; }

    /// <summary>A session is planning or running: the page's add box sends to This session by default (owner, AB-Improvements #23).</summary>
    bool IsSessionOn => false;

    /// <summary>The live label categories (ADR 93), in their order: the "@" list groups the labels under them.</summary>
    IReadOnlyList<TaskLabelGroup> LabelGroups => [];

    /// <summary>
    /// Details (or Ctrl+Enter while typing) in the Tasks page's box (Tasks redesign item 5): the task editor popup opens for a new task
    /// with what <paramref name="box"/> holds. Nothing where there is no editor.
    /// </summary>
    void OpenDetails(AddBoxViewModel box)
    {
    }
}
