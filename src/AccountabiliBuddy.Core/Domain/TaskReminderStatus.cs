using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>What happened to one occurrence of a task reminder (ADR 76); it syncs, so acting on one device stops it everywhere.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskReminderStatus>))]
public enum TaskReminderStatus
{
    [JsonStringEnumMemberName("up")]
    Up,

    [JsonStringEnumMemberName("snoozed")]
    Snoozed,

    [JsonStringEnumMemberName("dismissed")]
    Dismissed,

    [JsonStringEnumMemberName("started")]
    Started,
}
