using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A date's plan as agenda rows in the Exceptions tab, as the day really runs: last night's sleep up to the morning
/// comes first (from 00:00), tonight's sleep, which belongs to tomorrow's plan, comes last (until 24:00), and each part
/// is in its category's colour.
/// </summary>
public static class DayAgenda
{
    public static IReadOnlyList<AgendaRow> For(DayPlan plan, DateOnly date)
    {
        var day = new LocalWindow(date.ToDateTime(TimeOnly.MinValue), date.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return DayWorkWindows.PartsIn(plan, day, day.Start)
            .Select(part =>
            {
                Color color = Color.TryParse(part.Category.Color, out Color parsed) ? parsed : Colors.Gray;
                return new AgendaRow(part.Name, Times(part.Window, day), new SolidColorBrush(color), new SolidColorBrush(color, 0.18));
            })
            .ToList();
    }

    /// <summary>The plan a date follows when it has no one-off day: what the week map (A or B) gives its weekday.</summary>
    public static DayTemplate DefaultFor(DayPlan plan, DateOnly date)
    {
        return plan.WeekTemplateFor(date);
    }

    /// <summary>True when a one-off day's parts differ from the default plan its date would follow.</summary>
    public static bool DiffersFromDefault(DayPlan plan, OneOffDay day)
    {
        return !SameParts(day.Template, DefaultFor(plan, day.Date));
    }

    /// <summary>The same mode, budget and work window, and the same parts (category, times and label), whatever their ids and order.</summary>
    public static bool SameParts(DayTemplate first, DayTemplate second)
    {
        if (first.Mode != second.Mode || first.BudgetMinutes != second.BudgetMinutes || first.WorkWindow != second.WorkWindow)
        {
            return false;
        }

        static IEnumerable<(Guid CategoryId, TimeOnly Start, TimeOnly End, string? Label)> Keys(DayTemplate template) =>
            template.Parts
                .Select(part => (part.CategoryId, part.Start, part.End, part.Label))
                .OrderBy(key => key.Start).ThenBy(key => key.End).ThenBy(key => key.CategoryId);

        return Keys(first).SequenceEqual(Keys(second));
    }

    // Clipped to the day, in the app's time format: last night's sleep shows from 12:00 AM, tonight's until midnight.
    private static string Times(LocalWindow window, LocalWindow day)
    {
        string end = window.End >= day.End ? "midnight" : window.End.ToString("t");
        return $"{window.Start:t} – {end}";
    }
}
