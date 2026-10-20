namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// Which drift marks are under the pointer, in pixels as they're drawn: a mark is at least
/// <see cref="MinMarkWidth"/> wide, and the pointer may be <see cref="Slack"/> beside it, since a short drift is
/// narrower than a pixel at its real times.
/// </summary>
public static class DayBarHitTest
{
    public const double MinMarkWidth = 2;
    public const double Slack = 2;

    /// <param name="x">The pointer's position in the control.</param>
    /// <param name="barLeft">Where the bar starts (after the label column).</param>
    public static IReadOnlyList<DayBarSpan> MarksAt(IReadOnlyList<DayBarSpan> marks, double x, double barLeft, double barWidth)
    {
        var hits = new List<DayBarSpan>();
        foreach (DayBarSpan mark in marks)
        {
            double left = barLeft + (Math.Clamp(mark.Start, 0, 1) * barWidth);
            double right = Math.Max(left + MinMarkWidth, barLeft + (Math.Clamp(mark.End, 0, 1) * barWidth));
            if (x >= left - Slack && x <= right + Slack)
            {
                hits.Add(mark);
            }
        }

        return hits;
    }
}
