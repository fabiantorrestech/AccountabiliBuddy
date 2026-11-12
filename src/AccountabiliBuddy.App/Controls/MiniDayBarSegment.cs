using Avalonia.Media;

namespace AccountabiliBuddy.App.Controls;

/// <summary>One part of today on the Today sidebar's mini day bar, as shares of the bar's span (0 to 1), in its category's colour.</summary>
public readonly record struct MiniDayBarSegment(double From, double To, IBrush Brush);
