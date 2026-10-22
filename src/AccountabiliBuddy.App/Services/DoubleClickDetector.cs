using Avalonia;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Tells a double-click from two presses: the second press within <c>maxGap</c> of the first and within
/// <c>maxDistance</c> of where it was. A double-click starts over, so a third press is a new first press.
/// </summary>
public sealed class DoubleClickDetector
{
    private readonly TimeSpan _maxGap;
    private readonly double _maxDistance;
    private TimeSpan? _firstAt;
    private Point _firstPosition;

    public DoubleClickDetector(TimeSpan maxGap, double maxDistance)
    {
        _maxGap = maxGap;
        _maxDistance = maxDistance;
    }

    /// <summary>A press at <paramref name="at"/> (any steady clock); true when it completes a double-click.</summary>
    public bool Press(TimeSpan at, Point position)
    {
        if (_firstAt is TimeSpan firstAt && at - firstAt <= _maxGap && Distance(position, _firstPosition) <= _maxDistance)
        {
            Reset();
            return true;
        }

        _firstAt = at;
        _firstPosition = position;
        return false;
    }

    public void Reset()
    {
        _firstAt = null;
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
