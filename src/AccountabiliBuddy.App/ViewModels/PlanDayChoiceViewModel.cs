namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One coming day in the "Put in plan" flyout, with the parts of its plan that can still be chosen.</summary>
public sealed record PlanDayChoiceViewModel(string Label, IReadOnlyList<PlanPartChoiceViewModel> Parts);
