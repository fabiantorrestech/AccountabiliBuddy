namespace AccountabiliBuddy.App.Services;

/// <summary>What the running session's checklist did with a task change another device asked for (ADR 48).</summary>
/// <param name="Ok">Applied, parked instead, or nothing left to do; false when refused.</param>
/// <param name="Parked">A task the PC parked as a come-back instead of adding it (the checklist was full): the phone must not take it as added.</param>
/// <param name="Message">For the other device to show: why it was refused, or what happened instead (a parked 4th task).</param>
/// <param name="Invalid">The request itself made no sense (an empty title, too long); only meaningful when <paramref name="Ok"/> is false.</param>
public sealed record RemoteTaskResult(bool Ok, string? Message = null, bool Invalid = false, bool Parked = false)
{
    public static RemoteTaskResult Applied(string? message = null) => new(true, message);

    /// <summary>The task was parked as a come-back (only its title, category and a step count), not added to the checklist.</summary>
    public static RemoteTaskResult ParkedInstead(string message) => new(true, message, Parked: true);

    public static RemoteTaskResult Refused(string message) => new(false, message);

    public static RemoteTaskResult NotValid(string message) => new(false, message, Invalid: true);
}
