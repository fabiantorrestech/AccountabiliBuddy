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

    /// <summary>Adds the task. False when nothing was added (the box keeps the text).</summary>
    Task<bool> AddFromBoxAsync(AddBoxRequest request);

    /// <summary>Today's date on the trusted clock, in the PC's time zone (the jot's "N today").</summary>
    DateOnly Today { get; }
}
