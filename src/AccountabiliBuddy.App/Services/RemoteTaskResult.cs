namespace AccountabiliBuddy.App.Services;

/// <summary>What the running session's checklist did with a task change another device asked for (ADR 48).</summary>
/// <param name="Ok">Applied, parked instead, or nothing left to do; false when refused.</param>
/// <param name="Message">For the other device to show: why it was refused, or what happened instead (a parked 4th task).</param>
/// <param name="Invalid">The request itself made no sense (an empty title, too long); only meaningful when <paramref name="Ok"/> is false.</param>
public sealed record RemoteTaskResult(bool Ok, string? Message = null, bool Invalid = false)
{
    public static RemoteTaskResult Applied(string? message = null) => new(true, message);

    public static RemoteTaskResult Refused(string message) => new(false, message);

    public static RemoteTaskResult NotValid(string message) => new(false, message, Invalid: true);
}
