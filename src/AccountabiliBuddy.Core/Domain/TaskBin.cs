using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Where a task sits in the one shared task list (ADR 76): Today's three bins and the Shelf's three. Written in the sync file
/// and the stores as the lower-case names, which the phone's twin uses too.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskBin>))]
public enum TaskBin
{
    /// <summary>Today, This session: the actions the next or running session works on (capped, ADR 76).</summary>
    [JsonStringEnumMemberName("session")]
    Session,

    /// <summary>Today, Queued: waiting for a free place in the session.</summary>
    [JsonStringEnumMemberName("queued")]
    Queued,

    /// <summary>Today, Finished: ticked; it goes to History when the day is over.</summary>
    [JsonStringEnumMemberName("done")]
    Done,

    /// <summary>Shelf, To sort: jotted thoughts, come-backs, auto-saved and parked tasks.</summary>
    [JsonStringEnumMemberName("sort")]
    Sort,

    /// <summary>Shelf, Next: for the next few days.</summary>
    [JsonStringEnumMemberName("next")]
    Next,

    /// <summary>Shelf, Later: some other day (the old Later list, ADR 50).</summary>
    [JsonStringEnumMemberName("later")]
    Later,
}
