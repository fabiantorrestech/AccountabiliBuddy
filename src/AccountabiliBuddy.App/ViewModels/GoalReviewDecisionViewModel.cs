using System.Windows.Input;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One numbered decision of the weekly goals review (round 2 R5.4, board Goals-PC-Review): "1." Keep going, "2." Adjust the target,
/// "3." Pause this goal, "4." Decide next week. The highlighted one has the ❯ caret and the accent ring; the suggestion carries
/// "Suggested", an answer given before "Your choice". Off (faded, with why) when it can't apply: Deep work can't be paused.
/// </summary>
public sealed partial class GoalReviewDecisionViewModel(GoalReviewChoice choice, int number, string title, MaterialIconKind icon, ICommand pick) : ObservableObject
{
    public GoalReviewChoice Choice { get; } = choice;

    public string Number { get; } = number + ".";

    public string Title { get; } = title;

    public MaterialIconKind Icon { get; } = icon;

    public ICommand Pick { get; } = pick;

    /// <summary>"Same target · no change", or why it is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    public partial string Sub { get; set; } = string.Empty;

    public string Name => Number + " " + Title + ": " + Sub;

    [ObservableProperty]
    public partial bool IsLit { get; set; }

    [ObservableProperty]
    public partial bool IsSuggested { get; set; }

    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;
}
