using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the review card: a site or app with no rule, how long it was used, and what to do with it.</summary>
/// <param name="Site">The site (domain), or null for an app.</param>
/// <param name="App">The app (process name).</param>
public sealed partial class ReviewRowViewModel(string? site, string app, TimeSpan time) : ObservableObject
{
    public string? Site { get; } = site;

    public string App { get; } = app;

    public TimeSpan Time { get; } = time;

    /// <summary>The site, or the app without one.</summary>
    public string Name => Site ?? App;

    /// <summary>The key kept in "review.ignored" (lowercase).</summary>
    public string Key => Name.ToLowerInvariant();

    /// <summary>E.g. "12m on example.com with no rule".</summary>
    public string Text => $"{Duration(Time)} on {Name} with no rule";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDistracting), nameof(IsFine), nameof(IsIgnored))]
    public partial ReviewChoice Choice { get; set; }

    public bool IsDistracting
    {
        get => Choice == ReviewChoice.Distracting;
        set => Choose(ReviewChoice.Distracting, value);
    }

    public bool IsFine
    {
        get => Choice == ReviewChoice.Fine;
        set => Choose(ReviewChoice.Fine, value);
    }

    public bool IsIgnored
    {
        get => Choice == ReviewChoice.Ignore;
        set => Choose(ReviewChoice.Ignore, value);
    }

    /// <summary>The rule a choice writes: the whole site, or the app.</summary>
    public Rule RuleFor(RuleVerdict verdict)
    {
        return Site is not null
            ? new Rule(Guid.NewGuid(), MatchTarget.Url, MatchKind.Domain, Site, verdict, "From the session review")
            : new Rule(Guid.NewGuid(), MatchTarget.ProcessName, MatchKind.Exact, App, verdict, "From the session review");
    }

    private void Choose(ReviewChoice choice, bool selected)
    {
        if (selected)
        {
            Choice = choice;
        }
        else if (Choice == choice)
        {
            Choice = ReviewChoice.None;
        }
    }

    private static string Duration(TimeSpan time)
    {
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m" : $"{Math.Max(1, (int)Math.Round(time.TotalMinutes))}m";
    }
}
