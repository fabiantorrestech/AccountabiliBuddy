using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One entry of the goal dialog's left nav (round 2 R5.4): Basics, Target, Tracking &amp; sources, Reminders, On screen, Notes in Edit goal;
/// the five steps in New goal (Basics ✓ › Target (Step 2 of 5) › …). Its second line says what is set ("15 min a day · 50h by 1/31").
/// </summary>
public sealed partial class GoalEditorNavItem : ObservableObject
{
    public GoalEditorNavItem(GoalEditSection section, string title, MaterialIconKind icon)
    {
        Section = section;
        Title = title;
        Icon = icon;
    }

    public GoalEditSection Section { get; }

    public string Title { get; }

    public MaterialIconKind Icon { get; }

    [ObservableProperty]
    public partial string Sub { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>New goal: a step that is valid and behind the current one (drawn with ✓).</summary>
    [ObservableProperty]
    public partial bool IsDone { get; set; }

    public MaterialIconKind ShownIcon => IsDone ? MaterialIconKind.Check : Icon;

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(ShownIcon));
}
