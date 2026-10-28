using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's "Seen while planning" card: the sites and apps with no rule that turned up during the planning stage, one chip each
/// (a site's domain, an app's name), and a line saying they are asked about together when planning ends. It only reads what
/// <see cref="PlanFirstService"/> already holds for that stacked question; nothing more is collected. Shown in the planning
/// stage only (a first session's learning has its own review at the end), and only once something was seen.
/// </summary>
public sealed partial class SeenWhilePlanningViewModel : ObservableObject
{
    private readonly PlanFirstService _planFirst;
    private readonly Func<string, string> _nameFor;

    /// <param name="nameFor">The name to show for an app or site: the one History shows (the user's own name for it), else the real one.</param>
    public SeenWhilePlanningViewModel(PlanFirstService planFirst, Func<string, string> nameFor)
    {
        _planFirst = planFirst;
        _nameFor = nameFor;
        Subjects = [];
        Summary = string.Empty;
        _planFirst.HeldUnknownsChanged += (_, _) => Refresh();
        _planFirst.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Each domain or app name seen, oldest first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Subjects { get; set; }

    /// <summary>"2 sites with no rule. We'll ask about them together when planning ends, not one by one."</summary>
    [ObservableProperty]
    public partial string Summary { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    private void Refresh()
    {
        // The list is read again each second while planning (Changed), but only replaced when it differs, so the chips don't rebuild.
        IReadOnlyList<ActivitySnapshot> held = _planFirst.InStage ? _planFirst.HeldUnknowns : [];
        List<string> subjects = held.Select(snapshot => _nameFor(EnforcementEngine.ClassificationSubject(snapshot))).ToList();
        if (!subjects.SequenceEqual(Subjects))
        {
            Subjects = subjects;
            Summary = Describe(held);
        }

        IsVisible = subjects.Count > 0;
    }

    // "2 sites with no rule", "1 app with no rule", or "3 sites and apps with no rule".
    private static string Describe(IReadOnlyList<ActivitySnapshot> held)
    {
        if (held.Count == 0)
        {
            return string.Empty;
        }

        int sites = held.Count(snapshot => snapshot.Url is not null && UrlNormalizer.GetHost(snapshot.Url) is not null);
        string what = sites == held.Count ? (held.Count == 1 ? "1 site" : $"{held.Count} sites")
            : sites == 0 ? (held.Count == 1 ? "1 app" : $"{held.Count} apps")
            : $"{held.Count} sites and apps";
        return $"{what} with no rule. We'll ask about them together when planning ends, not one by one.";
    }
}
