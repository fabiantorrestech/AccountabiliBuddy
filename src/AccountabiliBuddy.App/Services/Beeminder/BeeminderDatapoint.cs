namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// One datapoint of a Beeminder goal as the app keeps it for a moment (ADR 84): its id, its day and its value. The
/// <see cref="Order"/> (Beeminder's timestamp) only breaks ties inside a day for "first" and "last"; it is never stored.
/// Comments and every other field are dropped by <see cref="BeeminderDatapointsParser"/>.
/// </summary>
public sealed record BeeminderDatapoint(string Id, DateOnly Day, double Value, long Order);
