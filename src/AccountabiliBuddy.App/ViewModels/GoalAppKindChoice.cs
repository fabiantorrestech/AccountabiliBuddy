using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One kind in the chip of a screen-time entry (ST2): what it is called on screen and what it stands for.</summary>
public sealed record GoalAppKindChoice(GoalAppMatchKind Kind, string Text)
{
    /// <summary>The four kinds, in the order the chip lists them.</summary>
    public static IReadOnlyList<GoalAppKindChoice> All { get; } =
    [
        new(GoalAppMatchKind.Name, "Name"),
        new(GoalAppMatchKind.Process, "PC app"),
        new(GoalAppMatchKind.Website, "Website"),
        new(GoalAppMatchKind.Package, "Phone app"),
    ];

    public static GoalAppKindChoice Of(GoalAppMatchKind kind) => All.First(choice => choice.Kind == kind);

    public override string ToString() => Text;
}
