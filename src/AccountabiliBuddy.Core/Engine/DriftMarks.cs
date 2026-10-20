using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Turns drift from the history into marks for the timeline: one mark per visit to a site.</summary>
public static class DriftMarks
{
    /// <summary>
    /// Joins stretches on the same site (<see cref="DriftStretch.Site"/>) when one starts within <paramref name="gap"/>
    /// of the previous one's end, so leaving and coming back soon is one visit, even with another site in between.
    /// </summary>
    /// <returns>Ordered by start.</returns>
    public static IReadOnlyList<DriftStretch> Merge(IReadOnlyList<DriftStretch> stretches, TimeSpan gap)
    {
        var marks = new List<DriftStretch>();
        foreach (IGrouping<string, DriftStretch> site in stretches.GroupBy(stretch => stretch.Site, StringComparer.OrdinalIgnoreCase))
        {
            DriftStretch? current = null;
            foreach (DriftStretch stretch in site.OrderBy(stretch => stretch.Start))
            {
                if (current is not null && stretch.Start - current.End <= gap)
                {
                    current = current with { End = stretch.End > current.End ? stretch.End : current.End };
                    continue;
                }

                if (current is not null)
                {
                    marks.Add(current);
                }

                current = stretch;
            }

            if (current is not null)
            {
                marks.Add(current);
            }
        }

        return marks.OrderBy(mark => mark.Start).ToList();
    }
}
