using System.Globalization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Theming;

/// <summary>The one-line summary under Settings → Appearance. Times are always 12-hour.</summary>
public static class AppearanceSummary
{
    public static string ThemeLabel(ThemeName theme) => theme switch
    {
        ThemeName.NightDesk => "Night Desk",
        ThemeName.Paper => "Paper",
        _ => "Instrument",
    };

    /// <param name="plan">The plan for the set times or sunset; null for Light, Dark and System.</param>
    public static string Build(ThemeName theme, LightDarkMode mode, TimeOnly darkAt, TimeOnly lightAt, bool hasPlace, DarkPlan? plan, DateTimeOffset now)
    {
        string name = ThemeLabel(theme);
        string setTimes = $"Dark from {Format(darkAt)} to {Format(lightAt)}";
        switch (mode)
        {
            case LightDarkMode.Light:
                return $"{name}, always light.";
            case LightDarkMode.Dark:
                return $"{name}, always dark.";
            case LightDarkMode.SetTime:
                return $"{name}. {setTimes}.";
            case LightDarkMode.Sunset when plan is null || !hasPlace || plan.UsedSetTimes:
                return $"{name}. {setTimes} until sunset can be worked out.";
            case LightDarkMode.Sunset:
                bool today = DateOnly.FromDateTime(plan.NextSwitch.DateTime) == DateOnly.FromDateTime(now.DateTime);
                return plan.IsDark
                    ? $"{name} after dark, light again at {Format(plan.NextSwitch)} {(today ? "this morning" : "tomorrow")}."
                    : $"{name} by day, dark at {Format(plan.NextSwitch)} {(today ? "tonight" : "tomorrow")}.";
            default:
                return $"{name}, light or dark as Windows has it.";
        }
    }

    public static string Format(TimeOnly time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string Format(DateTimeOffset moment) => moment.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
