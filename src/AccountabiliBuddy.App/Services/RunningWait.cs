namespace AccountabiliBuddy.App.Services;

/// <summary>
/// A wait that runs in the background (ADR 66): an armed end, an unlock's countdown or a new spare key's. Shown with its time
/// left on the on-screen tile, Today and the tray, with Cancel when <paramref name="Cancel"/> is set.
/// </summary>
/// <param name="Label">"Session ends", "Unlock (Lunch)", "New spare key".</param>
public sealed record RunningWait(string Key, string Label, DateTimeOffset ReadyAt, Func<Task>? Cancel);
