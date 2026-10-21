using Avalonia.Media;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One kind of planned work under the day bar, e.g. "Work 7h 00m", in its category's colour.</summary>
public sealed record PlanTotal(string Text, IBrush Brush);
