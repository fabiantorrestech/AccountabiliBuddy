using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>Why a task is in History (ADR 76).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskHistoryKind>))]
public enum TaskHistoryKind
{
    /// <summary>Ticked and its day is over (or a ticked Later item, come-back or old next-list task). "Do again" makes a copy in Queued.</summary>
    [JsonStringEnumMemberName("finished")]
    Finished,

    /// <summary>Put away without finishing. "Bring back" returns it to its last bin.</summary>
    [JsonStringEnumMemberName("archived")]
    Archived,

    /// <summary>Deleted. "Bring back" returns it to its last bin.</summary>
    [JsonStringEnumMemberName("deleted")]
    Deleted,
}
