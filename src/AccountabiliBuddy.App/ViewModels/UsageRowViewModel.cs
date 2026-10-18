namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One app or site in a History top list, with a bar relative to the list's largest entry.</summary>
/// <param name="Fraction">0–1, this entry's time divided by the top entry's time.</param>
public sealed record UsageRowViewModel(string Name, string TimeText, double Fraction);
