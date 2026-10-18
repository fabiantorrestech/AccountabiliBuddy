namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A way to lift a profile's blocks, e.g. "Off day": wait <see cref="Wait"/>, then the session ends and scheduled
/// blocks pause until the unlock relocks. Configured per profile; can't be changed during focus.
/// </summary>
public sealed record UnlockOption(Guid Id, string Name, TimeSpan Wait)
{
    public static UnlockOption CreateOffDay()
    {
        return new UnlockOption(Guid.NewGuid(), "Off day", TimeSpan.FromMinutes(10));
    }
}
