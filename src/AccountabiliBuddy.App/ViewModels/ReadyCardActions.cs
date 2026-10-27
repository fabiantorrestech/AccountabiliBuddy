namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the planning "Ready?" card's buttons do (set by <see cref="Services.PlanFirstService"/>).</summary>
/// <param name="Start">Start: planning ends and the focus countdown starts fresh.</param>
/// <param name="Extend">+2, +5 or +15: that many more planning minutes (still not focus time).</param>
public sealed record ReadyCardActions(Action Start, Action<int> Extend);
