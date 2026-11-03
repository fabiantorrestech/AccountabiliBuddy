using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One way a calendar can give its events a category, as the rule choice offers it (ADR 58).</summary>
public sealed record RuleModeChoice(CalendarRuleMode Mode, string Text)
{
    public static RuleModeChoice Off { get; } = new(CalendarRuleMode.Off, "Off");

    public static RuleModeChoice Keywords { get; } = new(CalendarRuleMode.Keywords, "Keywords");

    public static RuleModeChoice EveryEvent { get; } = new(CalendarRuleMode.EveryEvent, "Every event → category");

    public static IReadOnlyList<RuleModeChoice> All { get; } = [Off, Keywords, EveryEvent];

    public static RuleModeChoice Of(CalendarRuleMode mode) => All.First(choice => choice.Mode == mode);
}
