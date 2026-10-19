namespace AccountabiliBuddy.Core.Domain;

/// <summary>A stretch of local (wall-clock) time: <see cref="Start"/> inclusive, <see cref="End"/> exclusive.</summary>
public readonly record struct LocalWindow(DateTime Start, DateTime End)
{
    public bool Contains(DateTime localTime)
    {
        return localTime >= Start && localTime < End;
    }

    public bool Covers(LocalWindow other)
    {
        return other.Start >= Start && other.End <= End;
    }
}
