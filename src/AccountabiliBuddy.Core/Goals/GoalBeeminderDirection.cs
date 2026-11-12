using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Which way a Beeminder link goes (change note §4). Only <see cref="Read"/> works: <see cref="Send"/> and <see cref="Both"/> are
/// "Coming soon" until ADR 80 is accepted (ADR 43 writes nothing).
/// </summary>
[JsonConverter(typeof(GoalEnumConverter<GoalBeeminderDirection>))]
public enum GoalBeeminderDirection
{
    Read,
    Send,
    Both,
}
