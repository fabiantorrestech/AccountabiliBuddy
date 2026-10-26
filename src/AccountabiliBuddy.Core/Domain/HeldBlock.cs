namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A Cold Turkey block the app holds the keys to (ADR 39), by name: in which windows (every focus session the user
/// starts, Pre-Work, work hours, day plan categories by id) and with which lock in focus sessions and in the others.
/// A session's profile's expected blocks are held in that session too.
/// </summary>
public sealed record HeldBlock(
    string Name,
    bool InFocusSessions,
    bool InPreWork,
    bool InWorkHours,
    IReadOnlyList<Guid> Categories,
    string FocusLock = HeldBlock.Password,
    string OtherLock = HeldBlock.Timed)
{
    /// <summary>A rolling lock, renewed to 10 minutes ahead; lapses on its own when the app stops.</summary>
    public const string Timed = "timed";

    /// <summary>The app's password; ends only with it (or the user's spare key).</summary>
    public const string Password = "password";
}
