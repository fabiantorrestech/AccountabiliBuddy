using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Focus minutes by weekday and hour of the day over a range (the Stats page's Intensity view), with the hours the user
/// focuses best in, a second stretch that works, and the weakest hour in the working day.
/// </summary>
public sealed class IntensityGrid
{
    /// <summary>Monday to Sunday.</summary>
    public const int Rows = 7;

    private const int WindowHours = 2;
    private const double MinTotalMinutes = 60;

    private IntensityGrid(double[,] minutes, int firstHour, int lastHour, IReadOnlyList<int> driftPerHour)
    {
        Minutes = minutes;
        FirstHour = firstHour;
        LastHour = lastHour;
        double[] perHour = new double[24];
        for (int row = 0; row < Rows; row++)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                perHour[hour] += minutes[row, hour];
                TotalMinutes += minutes[row, hour];
                MaxCell = Math.Max(MaxCell, minutes[row, hour]);
            }
        }

        if (TotalMinutes < MinTotalMinutes)
        {
            return;
        }

        Peak = BestWindow(perHour, driftPerHour, excludeFrom: -1, excludeTo: -1);
        if (Peak is not null)
        {
            IntensityWindow? other = BestWindow(perHour, driftPerHour, Peak.StartHour, Peak.EndHour);
            SecondWind = other is not null && other.FocusMinutes >= Peak.FocusMinutes * 0.3 ? other : null;
        }

        Weakest = WeakestHour(perHour, driftPerHour);
    }

    /// <summary>Focus minutes, [Monday = 0 .. Sunday = 6, hour 0 .. 23].</summary>
    public double[,] Minutes { get; }

    public double TotalMinutes { get; }

    /// <summary>The most minutes in one cell.</summary>
    public double MaxCell { get; }

    /// <summary>The first hour the grid shows (6 AM unless focus began earlier).</summary>
    public int FirstHour { get; }

    /// <summary>The last hour the grid shows: 11 PM.</summary>
    public int LastHour { get; }

    public IntensityWindow? Peak { get; }

    public IntensityWindow? SecondWind { get; }

    public IntensityWindow? Weakest { get; }

    /// <summary>0 for no focus in the cell, up to 4 for the busiest.</summary>
    public int Level(int row, int hour)
    {
        double minutes = Minutes[row, hour];
        if (minutes <= 0 || MaxCell <= 0)
        {
            return 0;
        }

        double share = minutes / MaxCell;
        return share <= 0.25 ? 1 : share <= 0.5 ? 2 : share <= 0.8 ? 3 : 4;
    }

    /// <summary>Builds the grid from the focus windows inside the dates <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="driftStarts">When each drift visit began (local time), to count them per hour.</param>
    public static IntensityGrid Build(IReadOnlyList<LocalWindow> windows, DateOnly from, DateOnly to, IReadOnlyList<DateTime> driftStarts)
    {
        var minutes = new double[Rows, 24];
        DateTime start = from.ToDateTime(TimeOnly.MinValue);
        DateTime end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        foreach (LocalWindow window in windows)
        {
            DateTime at = window.Start < start ? start : window.Start;
            DateTime until = window.End > end ? end : window.End;
            while (at < until)
            {
                DateTime hourEnd = at.Date.AddHours(at.Hour + 1);
                DateTime pieceEnd = hourEnd < until ? hourEnd : until;
                minutes[((int)at.DayOfWeek + 6) % 7, at.Hour] += (pieceEnd - at).TotalMinutes;
                at = pieceEnd;
            }
        }

        var driftPerHour = new int[24];
        foreach (DateTime drift in driftStarts.Where(drift => drift >= start && drift < end))
        {
            driftPerHour[drift.Hour]++;
        }

        int first = 6;
        int last = 23;
        for (int row = 0; row < Rows; row++)
        {
            for (int hour = 0; hour < first; hour++)
            {
                if (minutes[row, hour] > 0)
                {
                    first = hour;
                }
            }
        }

        return new IntensityGrid(minutes, first, last, driftPerHour);
    }

    private IntensityWindow? BestWindow(double[] perHour, IReadOnlyList<int> driftPerHour, int excludeFrom, int excludeTo)
    {
        int bestStart = -1;
        double best = 0;
        for (int hour = 0; hour + WindowHours <= 24; hour++)
        {
            if (excludeFrom >= 0 && hour < excludeTo && hour + WindowHours > excludeFrom)
            {
                continue;
            }

            double sum = perHour.Skip(hour).Take(WindowHours).Sum();
            if (sum > best)
            {
                best = sum;
                bestStart = hour;
            }
        }

        return bestStart < 0 ? null : Window(bestStart, WindowHours, perHour, driftPerHour);
    }

    // The quietest hour inside the stretch where the user usually focuses (not its first or last hour, which only mark its edges).
    private IntensityWindow? WeakestHour(double[] perHour, IReadOnlyList<int> driftPerHour)
    {
        double busiest = perHour.Max();
        int[] active = Enumerable.Range(0, 24).Where(hour => perHour[hour] >= busiest * 0.2).ToArray();
        if (active.Length == 0 || active[^1] - active[0] < 3)
        {
            return null;
        }

        int weakest = -1;
        double least = double.MaxValue;
        for (int hour = active[0] + 1; hour < active[^1]; hour++)
        {
            if (perHour[hour] < least)
            {
                least = perHour[hour];
                weakest = hour;
            }
        }

        return weakest < 0 || least >= busiest * 0.6 ? null : Window(weakest, 1, perHour, driftPerHour);
    }

    private IntensityWindow Window(int start, int hours, double[] perHour, IReadOnlyList<int> driftPerHour)
    {
        double minutes = perHour.Skip(start).Take(hours).Sum();
        return new IntensityWindow(start, start + hours, minutes / TotalMinutes, minutes, driftPerHour.Skip(start).Take(hours).Sum());
    }
}
